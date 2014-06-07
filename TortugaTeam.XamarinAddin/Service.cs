using System;
using System.Net.Sockets;
using System.Net;
using System.Diagnostics;
using MonoDevelop.Core;
using System.Text;
using MonoDevelop.Ide;
using MonoDevelop.Ide.Gui;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using Newtonsoft.Json.Linq;

namespace TortugaTeam.XamarinAddin
{
	public sealed class Service : IDisposable
	{
		private class ConnectionState
        {
            public byte[] Buffer;
            public int Received;
			public int Expecting;
            public TcpClient Client;                        
        } 
			
		private static readonly Encoding encoding = new UTF8Encoding(false);
		private static Service current;

		private int port = 12312;
		private TcpListener listener;

		public static Service Current
		{
			get 
			{
				if (current == null) 
				{ 
					Initialize();
				}
				return current;
			}
		}

		public Service()
		{	
			var asm = Assembly.GetExecutingAssembly();
			var config = Path.Combine(Path.GetDirectoryName(asm.Location), asm.GetName().Name + ".config.json");
			if (File.Exists(config))
			{
				try
				{
					var obj = JObject.Parse(File.ReadAllText(config));

					JToken token;
					if (obj.TryGetValue("port", out token) && token.Type == JTokenType.Integer)
					{
						this.port = (int)token;
					}
				}
				catch (Exception ex)
				{
					LoggingService.LogError("Unity Tools: error reading config", ex);
				}
			}
			else
			{
				LoggingService.LogInfo("Unity Tools: config not found, using default");
			}


			try
			{
				this.listener = new TcpListener(IPAddress.Loopback, this.port);
			}
			catch (Exception ex)
			{
				LoggingService.LogError("Unity Tools listener creation failed", ex);
				return;
			}				

			try
			{
				this.listener.Start(5);
			}
			catch (Exception ex)
			{
				LoggingService.LogError("Unity Tools: listener start failed", ex);
				this.listener = null;
				return;
			}

			try
			{
				this.listener.BeginAcceptTcpClient(this.OnAcceptTcpClient, this.listener);
			}
			catch (Exception ex)
			{
				LoggingService.LogError("Unity Tools: listener begin accept client failed", ex);
				this.listener.Stop();
				this.listener = null;
				return;
			}				
				
			LoggingService.LogInfo("Unity Tools: listening...");
		}

		public static void Initialize()
		{
			if (current == null) 
			{
				current = new Service();
			}
		}

		public static void Shutdown()
		{
			if (current != null)
			{
				current.Dispose();
				current = null;
			}
		}

		public void Dispose()
		{
			if (this.listener != null)
			{
				this.listener.Stop();
				this.listener = null;
			}					
		}


		private void OnAcceptTcpClient(IAsyncResult ar)
		{
			var listener = (TcpListener)ar.AsyncState;
			TcpClient client = null;
			try
			{
				client = listener.EndAcceptTcpClient(ar);
			}
			catch (Exception ex)
			{
				LoggingService.LogError("Unity Tools: accept client failed", ex);
			}

			if (client != null)
			{
				//try
				//{
					var state = new ConnectionState
					{
						Client = client,
						Buffer = new byte[1024],
						Received = 0,
						Expecting = 4
					};
					client.Client.BeginReceive(state.Buffer, state.Received, state.Expecting, SocketFlags.None, this.OnReceiveMessageLength, state);
					//}
				//catch (Exception ex)
				//{
				//	client.Close();
				//}
			}

			try
			{
				this.listener.BeginAcceptTcpClient(this.OnAcceptTcpClient, this.listener);
			}
			catch (Exception ex)
			{
				LoggingService.LogError("Unity Tools: listener continue accept client failed", ex);
				this.Dispose();
				return;
			}
		}

		private void OnReceiveMessageLength(IAsyncResult ar)
		{
			var state = (ConnectionState)ar.AsyncState;
			int received = 0;
			try
			{
				received = state.Client.Client.EndReceive(ar);
			}
			catch
			{
			}

			if (received == 0)
			{
				state.Client.Close();
				return;
			}

			state.Received += received;

			if (state.Received < state.Expecting)
			{
				state.Client.Client.BeginReceive(state.Buffer, state.Received, state.Expecting - state.Received, SocketFlags.None, this.OnReceiveMessageLength, state);
				return;
			}

			int length = BitConverter.ToInt32(state.Buffer, 0);

			if (length <= 0 || length > state.Buffer.Length)
			{
				state.Client.Close();
				return;
			}

			state.Received = 0;
			state.Expecting = length;
			state.Client.Client.BeginReceive(state.Buffer, state.Received, state.Expecting, SocketFlags.None, this.OnReceiveMessageBody, state);
		}

		private void OnReceiveMessageBody(IAsyncResult ar)
		{
			var state = (ConnectionState)ar.AsyncState;
			int received = 0;
			try
			{
				received = state.Client.Client.EndReceive(ar);
			}
			catch
			{
			}

			if (received == 0)
			{
				state.Client.Close();
				return;
			}

			state.Received += received;

			if (state.Received < state.Expecting)
			{
				state.Client.Client.BeginReceive(state.Buffer, state.Received, state.Expecting - state.Received, SocketFlags.None, this.OnReceiveMessageLength, state);
				return;
			}				

			string msg = null;
			try
			{
				msg = encoding.GetString(state.Buffer, 0, state.Expecting);
			}
			catch
			{
				LoggingService.LogInfo("Unity Tools: message error");
			}
			
			this.SendResponse(state, msg != null);
					

			DispatchService.GuiDispatch(() => 
			{
				this.ProcessMessage(msg);
			});
		}

		private void SendResponse(ConnectionState state, bool success)
		{
			state.Received = 0;
			state.Expecting = 4;
			Encoding.ASCII.GetBytes(success ? "SUCC" : "FAIL", 0, 4, state.Buffer, 0);
			state.Client.Client.BeginSend(state.Buffer, 0, state.Expecting, SocketFlags.None, this.OnSendResponse, state);
		}

		private void OnSendResponse(IAsyncResult ar)
		{
			var state = (ConnectionState)ar.AsyncState;
			int sent = 0;
			try
			{
				sent = state.Client.Client.EndSend(ar);
			}
			catch
			{
			}

			if (sent == 0)
			{
				LoggingService.LogInfo("Unity Tools: send response error");
				state.Client.Close();
				return;
			}
			state.Received += sent;
			if (state.Received < state.Expecting)
			{
				state.Client.Client.BeginSend(state.Buffer, state.Received, state.Expecting, SocketFlags.None, this.OnSendResponse, state);
				return;
			}

			state.Client.Close();
		}

		private void ProcessMessage(string message)
		{
			try
			{
				var msg = Decode(message);

				string cmd;
				if (!msg.TryGetValue("command", out cmd))
				{
					LoggingService.LogError("Unity Tools: no command provided in '" + message + "'");
					return;
				}

				if (cmd == "open")
				{
					string path;
					if (!msg.TryGetValue("path", out path))
					{
						LoggingService.LogWarning(string.Format("Unity Tools: no path provided in '{0}'", message));
						return;
					}

					int line = 0;
					string lineStr;
					if (msg.TryGetValue("line", out lineStr))
					{
						int.TryParse(lineStr, out line);
					}						

					var files = new List<FileOpenInformation>();

					string solution;
					if (msg.TryGetValue("solution", out solution) && !string.IsNullOrWhiteSpace(solution))
					{
						var sol = IdeApp.ProjectOperations.CurrentSelectedSolution;
						if (sol == null || !string.Equals(solution, sol.FileName, StringComparison.InvariantCultureIgnoreCase))
						{
							files.Add(new FileOpenInformation(
								new FilePath(solution),
								null,
								0,
								0,
								OpenDocumentOptions.TryToReuseViewer | OpenDocumentOptions.BringToFront | OpenDocumentOptions.CenterCaretLine
							));
						}
					}

					files.Add(new FileOpenInformation(
						new FilePath(path),
						null,
						line,
						0,
						OpenDocumentOptions.TryToReuseViewer | OpenDocumentOptions.BringToFront | OpenDocumentOptions.CenterCaretLine
					));

					IdeApp.OpenFiles(files.ToArray());
				}
				else
				{
					LoggingService.LogWarning(string.Format("Unity Tools: unknown command '{0}' in '{1}'", cmd, message));
				}
			}
			catch (Exception ex)
			{
				LoggingService.LogError("Unity Tools: unable to process message '" + message + "'", ex);
			}
		}

		private Dictionary<string, string> Decode(string message)
		{
			var result = new Dictionary<string, string>();
			var parts = message.Split('|');
			foreach (var p in parts)
			{
				int i = p.IndexOf(':');
				if (i < 0)
				{
					continue;
				}

				string key = i > 0 ? DecodePart(p.Remove(i)) : "";
				string value = i < p.Length - 1 ? DecodePart(p.Substring(i + 1)) : "";
				result[key] = value;
			}

			return result;
		}

		private string DecodePart(string part)
		{
			return part.Replace("%1", "|").Replace("%2", ":").Replace("%0", "%");
		}
	}
}

