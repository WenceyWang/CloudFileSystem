using System ;
using System . Collections ;
using System . Collections . Generic ;
using System . Linq ;

namespace DreamRecorder . CloudFileSystem . FileSystem
{

	public class EnumerateDirectoryContext
	{

		public List <(string FileName , FileMetadata FileMetadata)> AvailableFiles { get ; set ; }

		public int Progress { get ; set ; }

	}

}
