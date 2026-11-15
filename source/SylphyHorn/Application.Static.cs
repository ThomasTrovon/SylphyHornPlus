using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MetroTrilithon.Linq;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using SylphyHorn.Interop;
using SylphyHorn.Properties;

namespace SylphyHorn
{
	partial class Application
	{
		public static CommandLineArgs Args { get; private set; }

		public static TelemetryClient TelemetryClient { get; }

		public new static Application Current => (Application)System.Windows.Application.Current;

		static Application()
		{
			// MetroRadiance still asks for the .NET Framework System.ServiceModel assembly.
			AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
			{
				if (args.Name.StartsWith("System.ServiceModel,", StringComparison.Ordinal))
				{
					return typeof(Application).Assembly;
				}
				return null;
			};
			AppDomain.CurrentDomain.UnhandledException += HandleUnhandledException;

			// Application Insights 3 builds an Azure Monitor exporter and throws when no connection string exists.
			// This fork does not ship one. Telemetry stays off.
			var telemetry = new TelemetryConfiguration
			{
				ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000",
				DisableTelemetry = true,
			};
			TelemetryClient = new TelemetryClient(telemetry);
			TelemetryClient.Context.Session.Id = Guid.NewGuid().ToString();
			TelemetryClient.Context.Device.OperatingSystem = Environment.OSVersion.ToString();
			TelemetryClient.Context.Component.Version = ProductInfo.VersionString;
#if DEBUG
			TelemetryClient.Context.User.Id = Environment.UserName;
#endif
			SetInstrumentationKey();
		}

		static partial void SetInstrumentationKey();

		private static void HandleUnhandledException(object sender, UnhandledExceptionEventArgs args)
		{
			if ((DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMinutes >= 3)
			{
				// 3 分以上生きてたら安定稼働と見做して再起動させる
				Restart();
			}
			else
			{
				ReportException("AppDomain", sender, args.ExceptionObject as Exception);
			}
		}

		private static void Restart()
		{
			var startupScheduler = new StartupScheduler();
			// The replacement starts before this process exits. Drop the mutex first or the new process sees the lock and quits.
			(Current as Application)?.ReleaseSingleInstance();
			if (startupScheduler.IsRunning)
			{
				startupScheduler.Restart();
				return;
			}

			if (Args != null)
			{
				var restartCount = Args.Restarted ?? 0;

				Process.Start(
					Environment.ProcessPath,
					Args.Options
						.Where(x => x.Key != Args.GetKey(nameof(CommandLineArgs.Restarted)))
						.Concat(EnumerableEx.Return(Args.CreateOption(nameof(CommandLineArgs.Restarted), (restartCount + 1).ToString())))
						.Select(x => x.ToString())
						.JoinString(" "));
			}
		}


		private static void ReportException(string caller, object sender, Exception exception)
		{
			try
			{
				var now = DateTimeOffset.Now;
				var path = Path.Combine(
					Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
					ProductInfo.Company,
					ProductInfo.Product,
					"ErrorReports",
					$"ErrorReport-{now:yyyyMMdd-HHmmss}-{now.Millisecond:000}.log");

				var message = $@"*** Error Report ({caller}) ***
{ProductInfo.Product} ver.{ProductInfo.VersionString}
{now}

{new SystemEnvironment()}

Sender:    {(sender is Type t ? t : sender?.GetType())?.FullName}
Exception: {exception?.GetType().FullName}

{exception}
";
				// ReSharper disable once AssignNullToNotNullAttribute
				Directory.CreateDirectory(Path.GetDirectoryName(path));
				File.AppendAllText(path, message);

				TelemetryClient.TrackException(exception);
				TelemetryClient.TrackTrace(message, SeverityLevel.Critical);
			}
			catch (Exception ex)
			{
				Debug.WriteLine(ex);
			}

			Current.Shutdown();
		}
	}
}
