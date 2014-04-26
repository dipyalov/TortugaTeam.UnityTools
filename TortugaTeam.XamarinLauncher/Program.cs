using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TortugaTeam.XamarinLauncher
{
	internal class Program
	{		
		private static readonly Encoding encoding = new UTF8Encoding(false);

		private static void Main(string[] args)
		{
			string file = null;
			if (args.Length >= 1)
			{
				file = args[0];
			}
			
			int line = 0;
			if (args.Length >= 2)
			{
				int.TryParse(args[1], out line);
			}

			if (file == null || !File.Exists(file))
			{				
				file = FindSolution();
				line = 0;
			}			
			
			if (!string.IsNullOrWhiteSpace(file) && File.Exists(file))
			{					
				if (!TryExistingXamarin(file, line))
				{
					RunXamarin(file);
				}
				return;
			}
		}

		private static string FindSolution()
		{
			string dir = Environment.CurrentDirectory;			
			string name = Path.GetFileName(dir);			
			
			string sln = Path.Combine(dir, name + ".sln");			
			if (File.Exists(sln))
			{
				return sln;
			}
			
			sln = Path.Combine(dir, name + "-csharp.sln");			
			if (File.Exists(sln))
			{
				return sln;
			}

			return null;
		}

		private static bool TryExistingXamarin(string path, int line)
		{							
			try
			{				
				var message = Encode(new Dictionary<string, string>
				{
					{ "command", "open" },
					{ "path", path },
					{ "line", line.ToString() }
				});

				using (var client = new TcpClient(AddressFamily.InterNetwork))
				{
					client.NoDelay = true;
					client.Connect(IPAddress.Loopback, 12312);
					using (var s = client.GetStream())
					{
						var length = BitConverter.GetBytes(message.Length);
						s.Write(length, 0, length.Length);
						var msg = encoding.GetBytes(message);
						s.Write(msg, 0, msg.Length);
						s.Flush();

						var response = new byte[4];
						int read = 0;
						while (read < response.Length)
						{
							int v = s.Read(response, read, response.Length - read);
							if (v == 0)
							{
								return false;
							}
							read += v;
						}
						string r = Encoding.ASCII.GetString(response);
						return r == "SUCC";
					}
				}
			}
			catch
			{
				return false;
			}										
		}
		
		private static string Encode(Dictionary<string, string> data)
		{
			var s = new StringBuilder();
			foreach (var p in data)
			{
				if (s.Length > 0)
				{
					s.Append("|");
				}
				s.Append(EncodePart(p.Key));
				s.Append(":");
				s.Append(EncodePart(p.Value));
			}
			return s.ToString();
		}

		private static string EncodePart(string part)
		{
			return part.Replace("%", "%0").Replace("|", "%1").Replace(":", "%2");
		}

		private static Process RunXamarin(string path)
		{
			var xamarin = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Xamarin Studio\bin\XamarinStudio.exe");
			if (!File.Exists(xamarin))
			{				
				return null;
			}
			var psi = new ProcessStartInfo(xamarin)
			{
				WorkingDirectory = Environment.CurrentDirectory,
				Arguments = QuoteArgument(path)
			};
			return Process.Start(psi);			
		}

		private static string QuoteArgument(string path)
		{
			return "\"" + path.Replace("\"", "\"\"") + "\"";
		}

		private bool WaitForClientSide(Process process, int sleepInterval, int idleTime, int waitTimeMilliseconds) 
		{
			

			int processIdleTime = 0;
			long prevProcessTicks;

			// Refresh the process info
			process.Refresh();
			prevProcessTicks = process.TotalProcessorTime.Ticks;
			do 
			{
				long newProcessTicks;
				// Sleep
				Thread.Sleep(sleepInterval);
				waitTimeMilliseconds -= sleepInterval;
				// Refresh the process info
				process.Refresh();

				// Store the process's total idle time
				newProcessTicks = process.TotalProcessorTime.Ticks;
				if (prevProcessTicks != newProcessTicks) 
				{
					prevProcessTicks = newProcessTicks;
					processIdleTime = 0;
				}
				else 
				{
					processIdleTime += sleepInterval;
				}

				if (waitTimeMilliseconds <= 0) 
				{
					return false;
				}
			}
			while (processIdleTime < idleTime);
			
			return true;
		}
	}
}
