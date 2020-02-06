using System ;
using System . Collections ;
using System . Collections . Generic ;
using System . Linq ;

using DreamRecorder . ToolBox . CommandLine ;

namespace DreamRecorder . CloudFileSystem
{

	public class ProgramSetting : SettingBase <ProgramSetting , ProgramSettingCatalog>
	{

		[SettingItem (
						 ( int ) ProgramSettingCatalog . Onedrive ,
						 nameof ( HttpProxy ) ,
						 "Http/s Proxy to Use." ,
						 true ,
						 null )]
		public string HttpProxy { get ; set ; }

		[SettingItem (
						 ( int ) ProgramSettingCatalog . Onedrive ,
						 nameof ( ClientId ) ,
						 "Client id of Registered App." ,
						 true ,
						 "" )]
		public string ClientId { get ; set ; }


		[SettingItem (
						 ( int ) ProgramSettingCatalog . Onedrive ,
						 nameof ( TenantId ) ,
						 "Tenant id of Registered App." ,
						 true ,
						 "" )]
		public string TenantId { get ; set ; }


		[SettingItem (
						 ( int ) ProgramSettingCatalog . Onedrive ,
						 nameof ( UserName ) ,
						 "Username to login Onedrive for business." ,
						 true ,
						 "" )]
		public string UserName { get ; set ; }

		[SettingItem (
						 ( int ) ProgramSettingCatalog . Onedrive ,
						 nameof ( Password ) ,
						 "Password to login Onedrive for business." ,
						 true ,
						 "" )]
		public string Password { get ; set ; }


		[SettingItem (
						 ( int ) ProgramSettingCatalog . Onedrive ,
						 nameof ( BlockDirectory ) ,
						 "BlockDirectory in Onedrive for business." ,
						 true ,
						 "" )]
		public string BlockDirectory { get ; set ; }

		[SettingItem (
						 ( int ) ProgramSettingCatalog . SqlServer ,
						 nameof ( SqlConnectionString ) ,
						 "Connection string to Sql Server." ,
						 true ,
						 "" )]
		public string SqlConnectionString { get ; set ; }

		[SettingItem (
						 ( int ) ProgramSettingCatalog . FileSystem ,
						 nameof ( VolumeLabel ) ,
						 "Label of Volume." ,
						 false ,
						 "CloudFileSystem" )]
		public string VolumeLabel { get ; set ; }

	}

}
