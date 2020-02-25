using System ;
using System . Collections ;
using System . Collections . Generic ;
using System . Linq ;

using DreamRecorder . ToolBox . CommandLine ;

namespace DreamRecorder . CloudFileSystem . PageBlobProviders . MicrosoftGraph
{

	public class MicrosoftGraphSetting
		: SettingBase <MicrosoftGraphSetting , MicrosoftGraphSettingCatalog>
	{

		[SettingItem (
						 ( int ) MicrosoftGraphSettingCatalog . Onedrive ,
						 nameof ( ClientId ) ,
						 "Client id of Registered App." ,
						 true ,
						 "" )]
		public string ClientId { get ; set ; }


		[SettingItem (
						 ( int ) MicrosoftGraphSettingCatalog . Onedrive ,
						 nameof ( TenantId ) ,
						 "Tenant id of Registered App." ,
						 true ,
						 "" )]
		public string TenantId { get ; set ; }


		[SettingItem (
						 ( int ) MicrosoftGraphSettingCatalog . Onedrive ,
						 nameof ( UserName ) ,
						 "Username to login Onedrive for business." ,
						 true ,
						 "" )]
		public string UserName { get ; set ; }

		[SettingItem (
						 ( int ) MicrosoftGraphSettingCatalog . Onedrive ,
						 nameof ( Password ) ,
						 "Password to login Onedrive for business." ,
						 true ,
						 "" )]
		public string Password { get ; set ; }


		[SettingItem (
						 ( int ) MicrosoftGraphSettingCatalog . Onedrive ,
						 nameof ( BlockDirectory ) ,
						 "BlockDirectory in Onedrive for business." ,
						 true ,
						 "" )]
		public string BlockDirectory { get ; set ; }

	}

}
