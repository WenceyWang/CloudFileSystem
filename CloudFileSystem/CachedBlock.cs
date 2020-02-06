using System ;
using System . Collections ;
using System . Collections . Generic ;
using System . Linq ;

namespace DreamRecorder . CloudFileSystem
{

	public class CachedBlock
	{

		public BlockMetadata Metadata { get ; set ; }

		public long Sequence { get ; set ; }

		public bool IsModified { get ; set ; }

		public byte [ ] Content { get ; set ; }

	}

}
