using System;
using MonoDevelop.Components.Commands;
using MonoDevelop.Ide;
using System.Collections.Generic;
using MonoDevelop.Ide.Gui;
using MonoDevelop.Core;

namespace TortugaTeam.XamarinAddin
{
	public class StartupHandler : CommandHandler
	{
		protected override void Run()
		{
			base.Run();

			IdeApp.Exiting += (object sender, ExitEventArgs args) => 
			{
				Service.Shutdown();
			};
				
			string file = null;
			string line = null;
			string solution = null;
			var cargs = Environment.GetCommandLineArgs();
			int i = 1;
			while (i < cargs.Length - 1)
			{
				var a = cargs[i];
				if (a == "--tortuga-team-file")
				{
					file = cargs[i + 1];
					i++;
				}
				else if (a == "--tortuga-team-line")
				{
					line = cargs[i + 1];
					i++;
				}
				else if (a == "--tortuga-team-solution")
				{
					solution = cargs[i + 1];
					i++;
				}
				i++;
			}

			if (!string.IsNullOrWhiteSpace(file) || !string.IsNullOrWhiteSpace(solution))
			{
				IdeApp.Initialized += (object sender, EventArgs e) =>
				{
					var files = new List<FileOpenInformation>();

					if (!string.IsNullOrWhiteSpace(solution))
					{
						files.Add(new FileOpenInformation(
							new FilePath(solution),
							null,
							0,
							0,
							OpenDocumentOptions.TryToReuseViewer | OpenDocumentOptions.BringToFront | OpenDocumentOptions.CenterCaretLine
						));
					}

					if (!string.IsNullOrWhiteSpace(file))
					{
						int lineNum;
						if (line == null || !int.TryParse(line, out lineNum))
						{
							lineNum = 0;
						}

						files.Add(new FileOpenInformation(
							new FilePath(file),
							null,
							lineNum,
							0,
							OpenDocumentOptions.TryToReuseViewer | OpenDocumentOptions.BringToFront | OpenDocumentOptions.CenterCaretLine
						));
					}

					IdeApp.OpenFiles(files.ToArray());
				};
			}

			Service.Initialize();
		}			
	}
}

