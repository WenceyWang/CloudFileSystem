using System ;
using System.Collections ;
using System.Collections.Generic ;
using System . ComponentModel . DataAnnotations ;
using System.Linq ;

namespace DreamRecorder . CloudFileSystem
{

	public class BlockMetadata
	{
		
		[Key]
		public Guid Guid { get; set; }

		public string RemoteFileId { get ; set ; }

		[Required]
		public Guid File { get ; set ; }

		[Required]
		public long BlockSequence { get ; set ; }

	}

}