using System ;
using System . Collections ;
using System . Collections . Generic ;
using System . ComponentModel . DataAnnotations ;
using System . Linq ;

using Fsp . Interop ;

namespace DreamRecorder . CloudFileSystem . FileSystem
{

	public class FileMetadata
	{

		[Key]
		public Guid Guid { get ; set ; }

		[Required]
		public string Name { get ; set ; }

		[Required]
		public long Size { get ; set ; }

		[Required]
		public int AllocatedBlockCount { get ; set ; }

		public long AllocatedSize => AllocatedBlockCount * CloudFileSystem . BlockSize ;

		[Required]
		public uint Attributes { get ; set ; }

		[Required]
		public uint ReparseTag { get ; set ; }

		[Required]
		public ulong CreationTime { get ; set ; }

		[Required]
		public ulong LastAccessTime { get ; set ; }

		[Required]
		public ulong LastWriteTime { get ; set ; }

		[Required]
		public ulong ChangeTime { get ; set ; }

		[Required]
		public ulong IndexNumber { get ; set ; }

		[Required]
		public uint HardLinks { get ; set ; }

		[Required]
		public byte [ ] SecurityInfo { get ; set ; }

		[Required]
		public bool IsDeleted { get ; set ; }

		public FileInfo FileInfo
			=> new FileInfo
			   {
				   AllocationSize = ( ulong ) AllocatedSize ,
				   ChangeTime     = ChangeTime ,
				   CreationTime   = CreationTime ,
				   FileAttributes = Attributes ,
				   FileSize       = ( ulong ) Size ,
				   HardLinks      = 0 ,
				   IndexNumber    = 0 ,
				   LastAccessTime = LastAccessTime ,
				   LastWriteTime  = LastWriteTime ,
				   ReparseTag     = ReparseTag
			   } ;

	}

}
