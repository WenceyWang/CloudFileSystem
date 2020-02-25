using System ;
using System . Collections ;
using System . Collections . Generic ;
using System . Linq ;

using DreamRecorder . CloudFileSystem . PageBlobProviders . MicrosoftGraph ;
using DreamRecorder . ToolBox . CommandLine ;

namespace DreamRecorder . CloudFileSystem . Program
{

	public class ProgramSetting : SettingBase <ProgramSetting , ProgramSettingCatalog>
	{

		[SettingItem (
						 ( int ) ProgramSettingCatalog . Common ,
						 nameof ( HttpProxy ) ,
						 "Http/s Proxy to Use." ,
						 true ,
						 null )]
		public string HttpProxy { get ; set ; }

		[SettingItem (
						 ( int ) ProgramSettingCatalog . Common ,
						 nameof ( EncryptFileBlock ) ,
						 "Encrypt File Block." ,
						 true ,
						 false )]
		public bool EncryptFileBlock { get ; set ; }


		[SettingItem (
						 ( int ) ProgramSettingCatalog . PageBlob ,
						 nameof ( PageBlob ) ,
						 "Page Blob to Use." ,
						 true ,
						 nameof ( MicrosoftGraphBlobProvider ) )]
		public string PageBlob { get ; set ; }


		[SettingItem (
						 ( int ) ProgramSettingCatalog . PageBlob ,
						 nameof ( ParallelOperationLimit ) ,
						 "Parallel Upload Limit." ,
						 true ,
						 20 )]
		public int ParallelOperationLimit { get ; set ; }


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
