using System;
using MonoDevelop.Components.Commands;
using MonoDevelop.Ide;

namespace TortugaTeam.XamarinAddin
{
	public class StartupHandler : CommandHandler
	{
		protected override void Run()
		{
			base.Run();
			Service.Initialize();
			IdeApp.Exiting += (object sender, ExitEventArgs args) => 
			{
				Service.Shutdown();
			};
		}			
	}
}

