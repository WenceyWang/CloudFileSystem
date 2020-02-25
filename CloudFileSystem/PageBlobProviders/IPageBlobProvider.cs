using System ;
using System . Collections ;
using System . Collections . Generic ;
using System . Linq ;
using System . Threading . Tasks ;

namespace DreamRecorder . CloudFileSystem . PageBlobProviders
{

	public interface IPageBlobProvider
	{

		Quota GetQuota ( ) ;

		Task UploadFile ( string fileName , byte [ ] data ) ;

		Task <byte [ ]> DownloadFile ( string fileName ) ;

	}

}
