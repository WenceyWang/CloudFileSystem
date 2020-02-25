using System ;
using System . Collections ;
using System . Collections . Generic ;
using System . Linq ;
using System . Net ;
using System . Net . Http ;

using DreamRecorder . CloudFileSystem . PageBlobProviders ;
using DreamRecorder . ToolBox . CommandLine ;
using DreamRecorder . ToolBox . General ;

using Microsoft . Extensions . CommandLineUtils ;
using Microsoft . Extensions . DependencyInjection ;
using Microsoft . Extensions . Logging ;
using Microsoft . Graph ;

using WenceyWang . FIGlet ;

namespace DreamRecorder . CloudFileSystem . Program
{

	public class Program
		: ProgramBase <Program , ProgramExitCode , ProgramSetting , ProgramSettingCatalog>
	{

		public GraphServiceClient GraphServiceClient { get ; set ; }

		public override bool WaitForExit => true ;

		//public async Task OneDriveUploadLargeFile ( )
		//{
		//	try
		//	{
		//		byte buff = new byte ( ) ;
		//		using ( System . IO . MemoryStream ms = new System . IO . MemoryStream ( buff ) )
		//		{
		//			// Describe the file to upload. Pass into CreateUploadSession, when the service works as expected.
		//			//var props = new DriveItemUploadableProperties();
		//			//props.Name = "_hamilton.png";
		//			//props.Description = "This is a pictureof Mr. Hamilton.";
		//			//props.FileSystemInfo = new FileSystemInfo();
		//			//props.FileSystemInfo.CreatedDateTime = System.DateTimeOffset.Now;
		//			//props.FileSystemInfo.LastModifiedDateTime = System.DateTimeOffset.Now;

		//			// Get the provider. 
		//			// POST /v1.0/drive/items/01KGPRHTV6Y2GOVW7725BZO354PWSELRRZ:/_hamiltion.png:/microsoft.graph.createUploadSession
		//			// The CreateUploadSesssion action doesn't seem to support the options stated in the metadata.
		//			var uploadSession =
		//				await graphClient . Drive . Items [ "01KGPRHTV6Y2GOVW7725BZO354PWSELRRZ" ] .
		//									ItemWithPath ( "_hamilton.png" ) .
		//									CreateUploadSession ( ) .
		//									Request ( ) .
		//									PostAsync ( ) ;

		//			int maxChunkSize =
		//				320 * 1024 ; // 320 KB - Change this to your chunk size. 5MB is the default.
		//			ChunkedUploadProvider provider =
		//				new ChunkedUploadProvider (
		//											uploadSession ,
		//											graphClient ,
		//											ms ,
		//											maxChunkSize ) ;

		//			// Setup the chunk request necessities
		//			IEnumerable <UploadChunkRequest> chunkRequests =
		//				provider . GetUploadChunkRequests ( ) ;
		//			byte [ ]         readBuffer        = new byte[ maxChunkSize ] ;
		//			List <Exception> trackedExceptions = new List <Exception> ( ) ;
		//			DriveItem        itemResult        = null ;

		//			//upload the chunks
		//			foreach ( UploadChunkRequest request in chunkRequests )
		//			{
		//				// Do your updates here: update progress bar, etc.
		//				// ...
		//				// Send chunk request
		//				UploadChunkResult result =
		//					await provider . GetChunkRequestResponseAsync (
		//																	request ,
		//																	readBuffer ,
		//																	trackedExceptions ) ;

		//				if ( result . UploadSucceeded )
		//				{
		//					itemResult = result . ItemResponse ;
		//				}
		//			}

		//			// Check that upload succeeded
		//			if ( itemResult == null )
		//			{
		//				// Retry the upload
		//				// ...
		//			}
		//		}
		//	}
		//	catch ( ServiceException e )
		//	{
		//		Console . WriteLine ( e ) ;
		//		throw ;
		//	}
		//}

		public CloudFileSystemService Service { get ; set ; }

		public override string License
			=> typeof ( Program ) . GetResourceFile ( @"License.AGPL.txt" ) ;

		public override bool CanExit { get ; }

		public override bool HandleInput => true ;

		public override bool LoadSetting => true ;

		public override bool AutoSaveSetting => true ;

		public override bool LoadPlugin => true ;

		public static ITaskDispatcher TaskDispatcher { get ; private set ; }

		public HttpClientHandler HttpClientHandler
		{
			get
			{
				HttpClientHandler result = new HttpClientHandler ( ) ;

				if ( ! string . IsNullOrWhiteSpace ( Setting . HttpProxy ) )
				{
					result . Proxy                 = new WebProxy ( Setting . HttpProxy ) ;
					result . UseDefaultCredentials = true ;
				}

				return result ;
			}
		}

		public static void Main ( string [ ] args ) { new Program ( ) . RunMain ( args ) ; }

		public override void RegisterArgument ( CommandLineApplication application )
		{
			application . Option ( "-d" , "Debug Flags" , CommandOptionType . SingleValue ) ;

			application . Option ( "-D" , "Debug Log File" , CommandOptionType . SingleValue ) ;

			application . Option (
								  "-u" ,
								  "UNC prefix (single backslash)" ,
								  CommandOptionType . SingleValue ) ;

			application . Option ( "-m" , "Mount Point" , CommandOptionType . SingleValue ) ;
		}

		public override void Start ( string [ ] args )
		{
			TaskDispatcher = StaticServiceProvider . Provider . GetService <ITaskDispatcher> ( ) ;

			TaskDispatcher . Start ( ) ;

			Type pageBlobProviderType = AppDomain . CurrentDomain . GetAssemblies ( ) .
													SelectMany (
																assembly
																	=> assembly . GetTypes ( ) ) .
													Where (
														   type
															   => typeof ( IPageBlobProvider ) .
																   IsAssignableFrom ( type ) ) .
													FirstOrDefault (
																	type
																		=> type . Name
																		== Setting . PageBlob ) ;

			if ( pageBlobProviderType is null )
			{
				Logger . LogCritical ( "Can not found Type named {0}." , Setting . PageBlob ) ;

				Exit ( ProgramExitCode . InvalidSetting ) ;
				return ;
			}
			else
			{
				Logger . LogInformation (
										 "Using {0}." ,
										 pageBlobProviderType . AssemblyQualifiedName ) ;

				StaticServiceProvider . ServiceCollection . AddSingleton <IPageBlobProvider> (
																							  (
																								  IPageBlobProvider
																							  ) Activator .
																								  CreateInstance (
																												  pageBlobProviderType ) ) ;

				StaticServiceProvider . Update ( ) ;
			}

			if ( string . IsNullOrWhiteSpace ( Setting . SqlConnectionString ) )
			{
				Logger . LogCritical (
									  "Current setting file lacks required settings, check setting file and start program again." ) ;

				Exit ( ProgramExitCode . InvalidSetting ) ;
				return ;
			}

			if ( IsRunning )
			{
				Service                = new CloudFileSystemService ( ) ;
				Environment . ExitCode = Service . Run ( ) ;
			}

			Exit ( ) ;
		}

		public override void ConfigureLogger ( ILoggingBuilder builder )
		{
			builder . AddFilter ( level => level >= LogLevel . Information ) .
					  AddDebug ( ) .
					  AddConsole ( ) ;
		}

		public override void ShowLogo ( )
		{
			Console . WriteLine (
								 new AsciiArt (
											   nameof ( FileSystem . CloudFileSystem ) ,
											   width : CharacterWidth . Smush ) . ToString ( ) ) ;
		}

		public override void ShowCopyright ( )
		{
			Console . WriteLine (
								 $"Dream Recovery {nameof ( FileSystem . CloudFileSystem )} Copyright (C) 2020 - {DateTime . Now . Year} Wencey Wang" ) ;

			Console . WriteLine ( @"This program comes with ABSOLUTELY NO WARRANTY." ) ;

			Console . WriteLine (
								 @"This is free software, and you are welcome to redistribute it under certain conditions; read License.txt for details." ) ;
		}

		public override void OnExit ( ProgramExitCode code ) { TaskDispatcher . Stop ( ) ; }

	}

}
