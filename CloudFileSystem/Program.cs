using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;

using DreamRecorder.ToolBox.CommandLine;
using DreamRecorder.ToolBox.General;

using Microsoft.Extensions.CommandLineUtils;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Auth;
using Microsoft.Identity.Client;

using WenceyWang.FIGlet;

namespace DreamRecorder.CloudFileSystem
{

	public class Program
		: ProgramBase<Program, ProgramExitCode, ProgramSetting, ProgramSettingCatalog>
	{

		public static void Main(string[] args) { new Program().RunMain(args); }

		public override bool MainThreadWait => false;

		public override void RegisterArgument(CommandLineApplication application)
		{
			application.Option("-d", "Debug Flags", CommandOptionType.SingleValue);

			application.Option("-D", "Debug Log File", CommandOptionType.SingleValue);

			application.Option("-u", "UNC prefix (single backslash)", CommandOptionType.SingleValue);

			application.Option("-m", "Mount Point", CommandOptionType.SingleValue);
		}

		public GraphServiceClient GraphServiceClient { get; set; }

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

		public CloudFileSystemService Service { get; set; }

		public static NetworkCredential GraphCredential
			=> new NetworkCredential(
									 Current.Setting.UserName,
									 Current.Setting.Password);


		public override async void Start(string[] args)
		{


			HttpClientHandler httpClientHandler = new HttpClientHandler();

			if (!string.IsNullOrWhiteSpace(Setting.HttpProxy))
			{
				httpClientHandler.Proxy = new WebProxy(Setting.HttpProxy);
				httpClientHandler.UseDefaultCredentials = true;
			}

			HttpProvider
				httpProvider =
					new HttpProvider(
									 httpClientHandler,
									 false); // Setting disposeHandler to true does not affect the behavior

			if (string.IsNullOrWhiteSpace(Setting.ClientId )|| string.IsNullOrWhiteSpace(Setting.TenantId)|| string.IsNullOrWhiteSpace(Setting.UserName)|| string.IsNullOrWhiteSpace(Setting.Password)|| string.IsNullOrWhiteSpace(Setting.BlockDirectory)|| string.IsNullOrWhiteSpace(Setting.SqlConnectionString) )
			{
				Logger . LogCritical ( "Current setting file lacks required settings, check setting file and start program again." ) ;

				Exit ( ProgramExitCode . InvalidSetting ) ;
			}

			IPublicClientApplication publicClientApplication = PublicClientApplicationBuilder.
															   Create(Setting.ClientId).
															   WithTenantId(Setting.TenantId).
															   Build();

			string[] scopes = new[] { "User.Read", "Sites.ReadWrite.All" };

			List<IAccount> accounts =
				(await publicClientApplication.GetAccountsAsync()).ToList();

			AuthenticationResult result = null;
			if (accounts.Any())
			{
				result = await publicClientApplication.AcquireTokenSilent(scopes, accounts.FirstOrDefault())
													  .ExecuteAsync();
			}
			else
			{
				try
				{

					result = await publicClientApplication.AcquireTokenByUsernamePassword(scopes,
																						  GraphCredential.UserName,
																						  GraphCredential.SecurePassword)
														  .ExecuteAsync();
				}
				catch (MsalUiRequiredException ex) when (ex.Message.Contains("AADSTS65001"))
				{
					// Here are the kind of error messages you could have, and possible mitigations

					// ------------------------------------------------------------------------
					// MsalUiRequiredException: AADSTS65001: The user or administrator has not consented to use the application
					// with ID '{appId}' named '{appName}'. Send an interactive authorization request for this user and resource.

					// Mitigation: you need to get user consent first. This can be done either statically (through the portal), 
					// or dynamically (but this requires an interaction with Azure AD, which is not possible with 
					// the username/password flow)
					// Statically: in the portal by doing the following in the "API permissions" tab of the application registration:
					// 1. Click "Add a permission" and add all the delegated permissions corresponding to the scopes you want (for instance
					// User.Read and User.ReadBasic.All)
					// 2. Click "Grant/revoke admin consent for <tenant>") and click "yes".
					// Dynamically, if you are not using .NET Core (which does not have any Web UI) by 
					// calling (once only) AcquireTokenInteractive.
					// remember that Username/password is for public client applications that is desktop/mobile applications.
					// If you are using .NET core or don't want to call AcquireTokenInteractive, you might want to:
					// - use device code flow (See https://aka.ms/msal-net-device-code-flow)
					// - or suggest the user to navigate to a URL to consent: https://login.microsoftonline.com/common/oauth2/v2.0/authorize?client_id={clientId}&response_type=code&scope=user.read
					// ------------------------------------------------------------------------


					// ------------------------------------------------------------------------
					// ErrorCode: invalid_grant
					// SubError: basic_action
					// MsalUiRequiredException: AADSTS50079: The user is required to use multi-factor authentication.
					// The tenant admin for your organization has chosen to oblige users to perform multi-factor authentication.
					// Mitigation: none for this flow
					// Your application cannot use the Username/Password grant.
					// Like in the previous case, you might want to use an interactive flow (AcquireTokenInteractive()), 
					// or Device Code Flow instead.
					// Note this is one of the reason why using username/password is not recommended;
					// ------------------------------------------------------------------------

					// ------------------------------------------------------------------------
					// ex.ErrorCode: invalid_grant
					// subError: null
					// Message = "AADSTS70002: Error validating credentials.
					// AADSTS50126: Invalid username or password
					// In the case of a managed user (user from an Azure AD tenant opposed to a
					// federated user, which would be owned
					// in another IdP through ADFS), the user has entered the wrong password
					// Mitigation: ask the user to re-enter the password
					// ------------------------------------------------------------------------

					// ------------------------------------------------------------------------
					// ex.ErrorCode: invalid_grant
					// subError: null
					// MsalServiceException: ADSTS50034: To sign into this application the account must be added to 
					// the {domainName} directory.
					// or The user account does not exist in the {domainName} directory. To sign into this application, 
					// the account must be added to the directory.
					// The user was not found in the directory
					// Explanation: wrong username
					// Mitigation: ask the user to re-enter the username.
					// ------------------------------------------------------------------------
				}
				catch (MsalServiceException ex) when (ex.ErrorCode == "invalid_request")
				{
					// ------------------------------------------------------------------------
					// AADSTS90010: The grant type is not supported over the /common or /consumers endpoints. 
					// Please use the /organizations or tenant-specific endpoint.
					// you used common.
					// Mitigation: as explained in the message from Azure AD, the authority you use in the application needs 
					// to be tenanted or otherwise "organizations". change the
					// "Tenant": property in the appsettings.json to be a GUID (tenant Id), or domain name (contoso.com) 
					// if such a domain is registered with your tenant
					// or "organizations", if you want this application to sign-in users in any Work and School accounts.
					// ------------------------------------------------------------------------

				}
				catch (MsalServiceException ex) when (ex.ErrorCode == "unauthorized_client")
				{
					// ------------------------------------------------------------------------
					// AADSTS700016: Application with identifier '{clientId}' was not found in the directory '{domain}'.
					// This can happen if the application has not been installed by the administrator of the tenant or consented 
					// to by any user in the tenant.
					// You may have sent your authentication request to the wrong tenant
					// Cause: The clientId in the appsettings.json might be wrong
					// Mitigation: check the clientId and the app registration
					// ------------------------------------------------------------------------
				}
				catch (MsalServiceException ex) when (ex.ErrorCode == "invalid_client")
				{
					// ------------------------------------------------------------------------
					// AADSTS70002: The request body must contain the following parameter: 'client_secret or client_assertion'.
					// Explanation: this can happen if your application was not registered as a public client application in Azure AD
					// Mitigation: in the Azure portal, edit the manifest for your application and set the `allowPublicClient` to `true`
					// ------------------------------------------------------------------------
				}
				catch (MsalServiceException)
				{
					throw;
				}

				catch (MsalClientException ex) when (ex.ErrorCode == "unknown_user_type")
				{
					// Message = "Unsupported User Type 'Unknown'. Please see https://aka.ms/msal-net-up"
					// The user is not recognized as a managed user, or a federated user. Azure AD was not
					// able to identify the IdP that needs to process the user
					throw new ArgumentException("U/P: Wrong username", ex);
				}
				catch (MsalClientException ex) when (ex.ErrorCode == "user_realm_discovery_failed")
				{
					// The user is not recognized as a managed user, or a federated user. Azure AD was not
					// able to identify the IdP that needs to process the user. That's for instance the case
					// if you use a phone number
					throw new ArgumentException("U/P: Wrong username", ex);
				}
				catch (MsalClientException ex) when (ex.ErrorCode == "unknown_user")
				{
					// the username was probably empty
					// ex.Message = "Could not identify the user logged into the OS. See http://aka.ms/msal-net-iwa for details."
					throw new ArgumentException("U/P: Wrong username", ex);
				}
				catch (MsalClientException ex) when (ex.ErrorCode == "parsing_wstrust_response_failed")
				{
					// ------------------------------------------------------------------------
					// In the case of a Federated user (that is owned by a federated IdP, as opposed to a managed user owned in an Azure AD tenant)
					// ID3242: The security token could not be authenticated or authorized.
					// The user does not exist or has entered the wrong password
					// ------------------------------------------------------------------------
				}
			}


			UsernamePasswordProvider authProvider =
				new UsernamePasswordProvider(
											 publicClientApplication, scopes
											 );

			GraphServiceClient = new GraphServiceClient(authProvider, httpProvider);


			User me = await GraphServiceClient.Me.Request().GetAsync();

			Logger.LogInformation("Login as {0}", me.UserPrincipalName);

			Service = new CloudFileSystemService();
			Environment.ExitCode = Service.Run();
		}

		public override void ConfigureLogger(ILoggingBuilder builder)
		{
			builder.AddFilter(level => true).AddDebug();
			builder.AddFilter(
								level
									=> level
										>= Microsoft.Extensions.Logging.LogLevel. Trace).
					AddConsole();
		}

		public override void ShowLogo()
		{
			Console.WriteLine(
								new AsciiArt(
											nameof(CloudFileSystem),
											width: CharacterWidth.Smush).ToString());
		}

		public override void ShowCopyright()
		{
			Console.WriteLine(
							  $"Dream Recovery {nameof(CloudFileSystem)} Copyright (C) 2020 - {DateTime.Now.Year} Wencey Wang");

			Console.WriteLine(@"This program comes with ABSOLUTELY NO WARRANTY.");

			Console.WriteLine(
							  @"This is free software, and you are welcome to redistribute it under certain conditions; read License.txt for details.");
		}

		public override void OnExit(ProgramExitCode code)
		{

		}

		public override string License
			=> typeof(Program).GetResourceFile(@"License.AGPL.txt");

		public override bool CanExit { get; }

		public override bool HandleInput => false;

		public override bool LoadSetting => true;

		public override bool AutoSaveSetting => true;

	}

}
