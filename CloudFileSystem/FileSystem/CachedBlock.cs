using System ;
using System . Collections ;
using System . Collections . Generic ;
using System . Linq ;
using System . Security . Cryptography ;
using System . Threading . Tasks ;

using DreamRecorder . CloudFileSystem . PageBlobProviders ;
using DreamRecorder . ToolBox . General ;

using Microsoft . Extensions . DependencyInjection ;
using Microsoft . Extensions . Logging ;

namespace DreamRecorder . CloudFileSystem . FileSystem
{

	public class CachedBlock
	{

		public IPageBlobProvider PageBlobProvider => CloudFileSystem . Current . PageBlobProvider ;

		public BlockMetadata Metadata { get ; set ; }

		public long Sequence { get ; set ; }

		public bool IsModified { get ; set ; }

		public byte [ ] Content { get ; set ; }

		private string RemoteFileName => $"{Metadata . Guid}.bin" ;

		public CachedBlock ( ) { }


		public async Task UpdateRemoteBlock ( )
		{
			if ( IsModified )
			{
				byte [ ] dataToUpload = null ;

				if ( Metadata . IsEncrypted )
				{
					using Aes aes = Aes . Create ( ) ;

					aes . Mode    = CipherMode . CBC ;
					aes . Padding = PaddingMode . PKCS7 ;

					using ICryptoTransform encryptor = aes . CreateEncryptor ( ) ;

					dataToUpload =
						encryptor . TransformFinalBlock ( Content , 0 , Content . Length ) ;

					Metadata . AesKey = aes . Key ;
					Metadata . AesIV  = aes . IV ;
				}
				else
				{
					lock ( Content )
					{
						dataToUpload = ( byte [ ] ) Content . Clone ( ) ;
					}
				}

				await PageBlobProvider . UploadFile ( RemoteFileName , dataToUpload ) ;

				IsModified = false ;

				Metadata . RemoteFileName = RemoteFileName ;

				CloudFileSystem . Current . FlushMetadata ( ) ;
			}
		}

		public async Task DownloadRemoteBlock ( )
		{
			if ( ! IsModified )
			{
				byte [ ] downloadedData = await PageBlobProvider . DownloadFile ( RemoteFileName ) ;

				if ( Metadata . IsEncrypted )
				{
					using Aes aes = Aes . Create ( ) ;

					aes . Key = Metadata . AesKey ;
					aes . IV  = Metadata . AesIV ;

					aes . Mode    = CipherMode . CBC ;
					aes . Padding = PaddingMode . PKCS7 ;

					using ICryptoTransform decryptor = aes . CreateDecryptor ( ) ;

					Content = decryptor . TransformFinalBlock (
															   downloadedData ,
															   0 ,
															   downloadedData . Length ) ;
				}
				else
				{
					Content = downloadedData ;
				}
			}
			else
			{
				throw new InvalidOperationException ( ) ;
			}
		}

		#region Logger

		private static ILogger Logger
			=> _logger ??= StaticServiceProvider . Provider . GetService <ILoggerFactory> ( ) .
												   CreateLogger <CachedBlock> ( ) ;


		private static ILogger _logger ;

		#endregion

	}

}
