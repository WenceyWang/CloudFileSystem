using System ;
using System . Collections ;
using System . Collections . Generic ;
using System . ComponentModel . DataAnnotations ;
using System . Linq ;

namespace DreamRecorder . CloudFileSystem . FileSystem
{

	public class BlockMetadata
	{

		[Key]
		public Guid Guid { get ; set ; }

		[Required]
		public Guid File { get ; set ; }

		[Required]
		public long BlockSequence { get ; set ; }

		public string RemoteFileName { get ; set ; }

		[Required]
		public bool IsEncrypted { get ; set ; }

		public byte [ ] AesKey { get ; set ; }

		public byte [ ] AesIV { get ; set ; }

	}

}
