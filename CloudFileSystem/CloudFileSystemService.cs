using System ;
using System . Collections ;
using System . Collections . Generic ;
using System . IO ;
using System . Linq ;

using Fsp ;

namespace DreamRecorder . CloudFileSystem
{

	public class CloudFileSystemService : Service
	{

		private class CommandLineUsageException : Exception
		{

			public CommandLineUsageException ( string message = null ) : base ( message )
				=> HasMessage = null != message ;

			public bool HasMessage { get ; }

		}

		public string ProgramName => $"{nameof ( DreamRecorder )}{nameof ( CloudFileSystem )}" ;

		public CloudFileSystemService ( ) : base ( nameof ( CloudFileSystemService ) ) { }

		protected override void OnStart ( string [ ] args )
		{
			try
			{
				string          debugLogFile    = null ;
				uint            debugFlags      = 0 ;
				string          volumePrefix    = null ;
				string          mountPoint      = null ;
				IntPtr          debugLogHandle  = ( IntPtr ) ( - 1 ) ;
				FileSystemHost  host            = null ;
				CloudFileSystem cloudFileSystem = null ;
				int             I ;

				for ( I = 1 ; args . Length > I ; I++ )
				{
					string arg = args [ I ] ;
					if ( '-' != arg [ 0 ] )
					{
						break ;
					}

					switch ( arg [ 1 ] )
					{
						case '?' :
							throw new CommandLineUsageException ( ) ;
						case 'd' :
							SelectArgument ( args , ref I , ref debugFlags ) ;
							break ;
						case 'D' :
							SelectArgument ( args , ref I , ref debugLogFile ) ;
							break ;
						case 'm' :
							SelectArgument ( args , ref I , ref mountPoint ) ;
							break ;
						case 'u' :
							SelectArgument ( args , ref I , ref volumePrefix ) ;
							break ;
						default :
							throw new CommandLineUsageException ( ) ;
					}
				}

				if ( args . Length > I )
				{
					throw new CommandLineUsageException ( ) ;
				}

				if ( null != volumePrefix )
				{
					I = volumePrefix . IndexOf ( '\\' ) ;
					if ( - 1                   != I
					  && volumePrefix . Length > I
					  && '\\'                  != volumePrefix [ I + 1 ] )
					{
						I = volumePrefix . IndexOf ( '\\' , I + 1 ) ;
						if ( - 1                   != I
						  && volumePrefix . Length > I + 1
						  && char . IsLetter ( volumePrefix [ I + 1 ] )
						  && '$' == volumePrefix [ I + 2 ] )
						{
						}
					}
				}

				if ( null == mountPoint )
				{
					throw new CommandLineUsageException ( ) ;
				}

				if ( null != debugLogFile )
				{
					if ( 0 > FileSystemHost . SetDebugLogFile ( debugLogFile ) )
					{
						throw new CommandLineUsageException ( "cannot open debug log file" ) ;
					}
				}

				host = new FileSystemHost ( new CloudFileSystem ( ) ) { Prefix = volumePrefix } ;

				if ( 0 > host . Mount ( mountPoint , null , true , debugFlags ) )
				{
					throw new IOException ( "cannot mount file system" ) ;
				}

				mountPoint = host . MountPoint ( ) ;
				Host       = host ;

				Log (
					 EVENTLOG_INFORMATION_TYPE ,
					 $"{ProgramName}{( ! string . IsNullOrEmpty ( volumePrefix ) ? " -u ": string . Empty )}{( ! string . IsNullOrEmpty ( volumePrefix ) ? volumePrefix: string . Empty )}  -m {mountPoint}" ) ;
			}
			catch ( CommandLineUsageException ex )
			{
				Log (
					 EVENTLOG_ERROR_TYPE ,
					 $"{( ex . HasMessage ? ex . Message + "\n": "" )}"
				   + $"usage: {ProgramName} OPTIONS\n"
				   + "\n"
				   + "options:\n"
				   + "    -d DebugFlags       [-1: enable all debug logs]\n"
				   + "    -D DebugLogFile     [file path; use - for stderr]\n"
				   + "    -u \\Server\\Share    [UNC prefix (single backslash)]\n"
				   + "    -m MountPoint       [X:|*|directory]\n" ) ;
			}
			catch ( Exception ex )
			{
				Log ( EVENTLOG_ERROR_TYPE , $"{ex . Message}" ) ;
				throw ;
			}
		}

		protected override void OnStop ( )
		{
			Host . Unmount ( ) ;
			Host = null ;
		}

		private static void SelectArgument ( string [ ] args , ref int index , ref string result )
		{
			if ( args . Length > ++index )
			{
				result = args [ index ] ;
			}
			else
			{
				throw new CommandLineUsageException ( ) ;
			}
		}

		private static void SelectArgument ( string [ ] args , ref int index , ref uint result )
		{
			if ( args . Length > ++index )
			{
				result = int . TryParse ( args [ index ] , out int r ) ? ( uint ) r: result ;
			}
			else
			{
				throw new CommandLineUsageException ( ) ;
			}
		}

		public FileSystemHost Host { get ; private set ; }

	}

}
