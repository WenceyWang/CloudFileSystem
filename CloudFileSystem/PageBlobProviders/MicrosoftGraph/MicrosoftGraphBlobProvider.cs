using System ;
using System . Collections ;
using System . Collections . Concurrent ;
using System . Collections . Generic ;
using System . IO ;
using System . Linq ;
using System . Net ;
using System . Net . Http ;
using System . Threading ;
using System . Threading . Tasks ;

using DreamRecorder . CloudFileSystem . Program ;
using DreamRecorder . ToolBox . General ;

using Microsoft . Extensions . DependencyInjection ;
using Microsoft . Extensions . Logging ;
using Microsoft . Graph ;
using Microsoft . Graph . Auth ;
using Microsoft . Identity . Client ;

using File = System . IO . File ;

namespace DreamRecorder . CloudFileSystem . PageBlobProviders . MicrosoftGraph
{

	public class MicrosoftGraphBlobProvider : IPageBlobProvider
	{

		public ConcurrentQueue <GraphServiceClient> GraphServiceClients { get ; set ; } =
			new ConcurrentQueue <GraphServiceClient> ( ) ;

		public string BlockDirectory { get ; set ; }

		public static string SettingFileName
			=> AppDomain . CurrentDomain . BaseDirectory + SettingFile ;

		public MicrosoftGraphSetting Setting { get ; set ; }

		public NetworkCredential GraphCredential
			=> new NetworkCredential ( Setting . UserName , Setting . Password ) ;

		public MicrosoftGraphBlobProvider ( )
		{
			Logger . LogInformation ( "Loading setting file." ) ;

			if ( File . Exists ( SettingFileName ) )
			{
				Logger . LogInformation ( "Setting file exists, Reading." ) ;

				try
				{
					using ( FileStream stream = File . OpenRead ( SettingFileName ) )
					{
						Setting = MicrosoftGraphSetting . Load ( stream ) ;
					}

					Logger . LogInformation ( "Setting file loaded." ) ;
				}
				catch ( Exception )
				{
					Logger . LogInformation ( "Setting file error, will use default value." ) ;
					Setting = MicrosoftGraphSetting . GenerateNew ( ) ;
				}
			}
			else
			{
				Logger . LogInformation ( "Setting file doesn't exists, generating new." ) ;
				Setting = MicrosoftGraphSetting . GenerateNew ( ) ;
				SaveSettingFile ( ) ;
			}

			if ( string . IsNullOrWhiteSpace ( Setting . ClientId )
			  || string . IsNullOrWhiteSpace ( Setting . TenantId )
			  || string . IsNullOrWhiteSpace ( Setting . UserName )
			  || string . IsNullOrWhiteSpace ( Setting . Password )
			  || string . IsNullOrWhiteSpace ( Setting . BlockDirectory ) )
			{
				Logger . LogCritical (
									  "Current setting file lacks required settings, check setting file and start program again." ) ;

				Program . Program . Current . Exit ( ProgramExitCode . InvalidSetting ) ;
			}

			Login ( ) . Wait ( ) ;
		}

		public const string SettingFile = "GraphSettings.ini" ;

		public Quota GetQuota ( )
		{
			try
			{
				GraphServiceClient graphServiceClient ;
				while ( ! GraphServiceClients . TryDequeue ( out graphServiceClient ) )
				{
					Thread . Sleep ( 1 ) ;
				}

				Drive driveInfo = graphServiceClient .
								  Me . Drive . Request ( ) .
								  WithMaxRetry ( 5 ) .
								  GetAsync ( ) .
								  Result ;

				GraphServiceClients . Enqueue ( graphServiceClient ) ;

				Quota quota = new Quota
							  {
								  TotalQuota =
									  driveInfo ? . Quota ? . Total ?? long . MaxValue ,
								  RemainingQuota = driveInfo ? . Quota ? . Remaining ?? 0 ,
							  } ;

				return quota ;
			}
			catch ( Exception e )
			{
				Logger . LogError ( e , "Update quota failed." ) ;
				throw ;
			}
		}

		public async Task UploadFile ( string fileName , byte [ ] data )
		{
			fileName = $"{Setting . BlockDirectory}\\{fileName}" ;

			MemoryStream dataStream = new MemoryStream ( data ) ;

			GraphServiceClient graphServiceClient ;
			while ( ! GraphServiceClients . TryDequeue ( out graphServiceClient ) )
			{
				Thread . Sleep ( 1 ) ;
			}

			await graphServiceClient . Me . Drive . Root . ItemWithPath ( fileName ) .
									   Content . Request ( ) .
									   WithMaxRetry ( 5 ) .
									   PutAsync <DriveItem> ( dataStream ) ;

			GraphServiceClients . Enqueue ( graphServiceClient ) ;
		}

		public async Task <byte [ ]> DownloadFile ( string fileName )
		{
			fileName = $"{Setting . BlockDirectory}\\{fileName}" ;

			Stream remoteFile = null ;

			GraphServiceClient graphServiceClient ;
			while ( ! GraphServiceClients . TryDequeue ( out graphServiceClient ) )
			{
				Thread . Sleep ( 1 ) ;
			}

			try
			{
				remoteFile = await graphServiceClient .
								   Me . Drive . Root . ItemWithPath ( fileName ) .
								   Content . Request ( ) .
								   WithMaxRetry ( 5 ) .
								   GetAsync ( ) ;
			}
			catch ( Exception e )
			{
				Logger . LogWarning ( e , "Error downloading {0}" , fileName ) ;
			}

			GraphServiceClients . Enqueue ( graphServiceClient ) ;

			if ( remoteFile != null )
			{
				byte [ ] blockData = new byte[ remoteFile . Length ] ;

				await remoteFile . ReadAsync ( blockData ) ;

				return blockData ;
			}
			else
			{
				throw new Exception ( ) ;
			}
		}

		public void SaveSettingFile ( )
		{
			string       config      = Setting ? . Save ( ) ;
			FileStream   settingFile = File . OpenWrite ( SettingFileName ) ;
			StreamWriter writer      = new StreamWriter ( settingFile ) ;
			writer . Write ( config ) ;
			writer . Dispose ( ) ;
		}

		public async Task CreateServiceClient ( )
		{
			HttpClientFactory httpClientFactory = new HttpClientFactory ( ) ;

			IPublicClientApplication publicClientApplication = PublicClientApplicationBuilder .
															   Create ( Setting . ClientId ) .
															   WithTenantId ( Setting . TenantId ) .
															   WithHttpClientFactory (
																					  httpClientFactory ) .
															   Build ( ) ;

			string [ ] scopes = { "User.Read" , "Sites.ReadWrite.All" } ;

			UsernamePasswordProvider authProvider =
				new UsernamePasswordProvider ( publicClientApplication , scopes ) ;

			HttpProvider httpProvider = new HttpProvider (
														  Program .
															  Program . Current .
															  HttpClientHandler ,
														  false ) ; // Setting disposeHandler to true does not affect the behavior

			GraphServiceClient graphServiceClient =
				new GraphServiceClient ( authProvider , httpProvider ) ;

			User me = await graphServiceClient .
							Me . Request ( ) .
							WithUsernamePassword (
												  GraphCredential . UserName ,
												  GraphCredential . SecurePassword ) .
							GetAsync ( ) ;

			GraphServiceClients . Enqueue ( graphServiceClient ) ;
		}

		public async Task Login ( )
		{
			int parallelOperationLimit = Math . Max (
													 Program .
														 Program . Current . Setting .
														 ParallelOperationLimit ,
													 1 ) ;

			Task [ ] tasks = new Task[ parallelOperationLimit ] ;

			for ( int i = 0 ; i < parallelOperationLimit ; i++ )
			{
				tasks [ i ] = CreateServiceClient ( ) ;
			}

			Task . WaitAll ( tasks ) ;

			GraphServiceClient graphServiceClient ;
			while ( ! GraphServiceClients . TryDequeue ( out graphServiceClient ) )
			{
				Thread . Sleep ( 1 ) ;
			}

			User me = await graphServiceClient . Me . Request ( ) . GetAsync ( ) ;
			;

			GraphServiceClients . Enqueue ( graphServiceClient ) ;


			Logger . LogInformation ( "Login as {0}" , me . UserPrincipalName ) ;
		}

		public class HttpClientFactory : IMsalHttpClientFactory
		{

			public HttpClient GetHttpClient ( )
				=> new HttpClient ( Program . Program . Current . HttpClientHandler ) ;

		}

		#region Logger

		private static ILogger Logger
			=> _logger ??= StaticServiceProvider . Provider . GetService <ILoggerFactory> ( ) .
												   CreateLogger <MicrosoftGraphBlobProvider> ( ) ;


		private static ILogger _logger ;

		#endregion

	}

}
