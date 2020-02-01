using System ;
using System . Collections ;
using System . Collections . Generic ;
using System . IO ;
using System . Linq ;
using System . Net ;
using System . Net . Http ;
using System . Runtime . InteropServices ;
using System . Security . AccessControl ;
using System . Security . Principal ;
using System . Text ;
using System . Text . RegularExpressions ;
using System . Threading . Tasks ;

using DreamRecorder . ToolBox . General ;

using Fsp ;
using Fsp . Interop ;

using JetBrains . Annotations ;

using Microsoft . EntityFrameworkCore ;
using Microsoft . Extensions . DependencyInjection ;
using Microsoft . Extensions . Logging ;
using Microsoft . Graph ;
using Microsoft . Graph . Auth ;
using Microsoft . Identity . Client ;

namespace DreamRecorder . CloudFileSystem
{

	public class CloudFileSystem : FileSystemBase
	{

		public ILogger Logger { get ; }

		public static long BlockSize => 2 * 1024 * 1024 ;

		public ProgramSetting Setting => Program . Current . Setting ;

		public static CloudFileSystem Current { get ; private set ; }

		public DataContext DataContext { get ; set ; }

		public Dictionary <Guid , FileNode> FileNodes { get ; set ; } =
			new Dictionary <Guid , FileNode> ( ) ;

		public GraphServiceClient GraphServiceClient => Program . Current . GraphServiceClient ;

		public VolumeInfo VolumeInfo
		{
			get
			{
				VolumeInfo volumeInfo = new VolumeInfo ( ) { } ;
				volumeInfo . SetVolumeLabel ( Setting . VolumeLabel ) ;

				Drive driveInfo = GraphServiceClient .
								  Me . Drive . Request ( ) .
								  GetAsync ( ) .
								  Result ;

				volumeInfo . TotalSize =
					( ulong ) ( driveInfo ? . Quota ? . Total ?? long . MaxValue ) ;

				volumeInfo . FreeSize = ( ulong ) ( driveInfo ? . Quota ? . Remaining ?? 0 ) ;

				return volumeInfo ;
			}
		}

		#region Create

		public FileMetadata CreateFile ( string   name ,
										 int      allocatedBlocks    = 0 ,
										 uint ?   attributes         = null ,
										 byte [ ] securityDescriptor = null )
		{
			DateTimeOffset createTime = DateTimeOffset . UtcNow ;

			FileMetadata file = new FileMetadata ( )
								{
									AllocatedBlockCount = allocatedBlocks ,
									Attributes =
										attributes ?? ( uint ) FileAttributes . Normal ,
									Name           = name ,
									ChangeTime     = ( ulong ) createTime . ToFileTime ( ) ,
									CreationTime   = ( ulong ) createTime . ToFileTime ( ) ,
									Guid           = Guid . NewGuid ( ) ,
									HardLinks      = 0 ,
									IndexNumber    = 0 ,
									LastAccessTime = ( ulong ) createTime . ToFileTime ( ) ,
									LastWriteTime  = ( ulong ) createTime . ToFileTime ( ) ,
									ReparseTag     = 0 ,
									SecurityInfo =
										securityDescriptor
									 ?? new FileSecurity ( ) . GetSecurityDescriptorBinaryForm ( ) ,
									Size = 0 ,
								} ;

			lock ( DataContext )
			{
				DataContext . FileMetadata . Add ( file ) ;

				DataContext . SaveChanges ( ) ;
			}

			return file ;
		}

		public FileMetadata CreateDirectory ( string   name ,
											  uint ?   attributes         = null ,
											  byte [ ] securityDescriptor = null )
		{
			DateTimeOffset createTime = DateTimeOffset . UtcNow ;

			FileMetadata file = new FileMetadata ( )
								{
									AllocatedBlockCount = 0 ,
									Attributes =
										attributes ?? ( uint ) FileAttributes . Directory ,
									Name           = name ,
									ChangeTime     = ( ulong ) createTime . ToFileTime ( ) ,
									CreationTime   = ( ulong ) createTime . ToFileTime ( ) ,
									Guid           = Guid . NewGuid ( ) ,
									HardLinks      = 0 ,
									IndexNumber    = 0 ,
									LastAccessTime = ( ulong ) createTime . ToFileTime ( ) ,
									LastWriteTime  = ( ulong ) createTime . ToFileTime ( ) ,
									ReparseTag     = 0 ,
									SecurityInfo =
										securityDescriptor
									 ?? new FileSecurity ( ) . GetSecurityDescriptorBinaryForm ( ) ,
									Size = 0 ,
								} ;

			lock ( DataContext )
			{
				DataContext . FileMetadata . Add ( file ) ;

				DataContext . SaveChanges ( ) ;
			}

			return file ;
		}

		public BlockMetadata CreateBlock ( Guid file , long sequence )
		{
			BlockMetadata blockMetadata = new BlockMetadata ( )
										  {
											  RemoteFileId  = null ,
											  File          = file ,
											  BlockSequence = sequence
										  } ;

			lock ( DataContext )
			{
				DataContext . BlockMetadata . Add ( blockMetadata ) ;

				DataContext . SaveChanges ( ) ;
			}

			return blockMetadata ;
		}

		#endregion

		public void ResizeFile ( [NotNull] FileNode file , long newSize )
		{
			if ( file == null )
			{
				throw new ArgumentNullException ( nameof ( file ) ) ;
			}

			FileMetadata metadata = file . Metadata ;

			if ( newSize > metadata . AllocatedSize )
			{
				int requiredBlockCount = ( int ) ( ( newSize + BlockSize - 1 ) / BlockSize ) ;

				if ( metadata . AllocatedBlockCount < requiredBlockCount )
				{
					for ( int sequenceNumber = metadata . AllocatedBlockCount ;
						  sequenceNumber < requiredBlockCount ;
						  sequenceNumber++ )
					{
						file . Blocks . Add ( CreateBlock ( metadata . Guid , sequenceNumber ) ) ;
					}

					metadata . AllocatedBlockCount = requiredBlockCount ;
				}
			}

			metadata . Size = newSize ;

			lock ( DataContext )
			{
				DataContext . SaveChanges ( ) ;
			}
		}

		public override int Init ( object host )
		{
			Logger . LogTrace ( $"{nameof ( Init )}" ) ;


			Current = this ;


			FileMetadata rootDirectoryMetadata =
				DataContext . FileMetadata . SingleOrDefault (
															  metadata
																  => metadata . Name == "\\" ) ;
			if ( rootDirectoryMetadata == null )
			{
				rootDirectoryMetadata = CreateDirectory (
														 "\\" ,
														 securityDescriptor : DefaultSecurity .
															 RootSecurity ) ;
			}

			if ( host is FileSystemHost fileSystemHost )
			{
				fileSystemHost . SectorSize                  = 4096 ;
				fileSystemHost . SectorsPerAllocationUnit    = 1 ;
				fileSystemHost . MaxComponentLength          = 255 ;
				fileSystemHost . FileInfoTimeout             = 100000 ;
				fileSystemHost . CaseSensitiveSearch         = true ;
				fileSystemHost . CasePreservedNames          = true ;
				fileSystemHost . UnicodeOnDisk               = true ;
				fileSystemHost . PersistentAcls              = true ;
				fileSystemHost . PostCleanupWhenModifiedOnly = true ;
				fileSystemHost . PassQueryDirectoryPattern   = true ;
				fileSystemHost . FlushAndPurgeOnCleanup      = true ;
				fileSystemHost . VolumeCreationTime =
					rootDirectoryMetadata . CreationTime ;
				fileSystemHost . VolumeSerialNumber = 0 ;
				fileSystemHost . FileSystemName     = nameof ( CloudFileSystem ) ;
			}

			Logger . LogInformation ( $"Filesystem Initialized." ) ;

			return STATUS_SUCCESS ;
		}

		#region Security

		public override int GetSecurityByName ( string       fileName ,
												out uint     fileAttributes ,
												ref byte [ ] securityDescriptor )
		{
			Logger . LogTrace ( $"{nameof ( GetSecurityByName )} of {fileName}" ) ;

			FileMetadata fileMetadata =
				DataContext . FileMetadata . SingleOrDefault ( file => file . Name == fileName ) ;

			if ( fileMetadata is null )
			{
				fileAttributes     = default ;
				securityDescriptor = default ;

				return STATUS_OBJECT_NAME_NOT_FOUND ;
			}
			else
			{
				fileAttributes     = fileMetadata . Attributes ;
				securityDescriptor = fileMetadata . SecurityInfo ;

				return STATUS_SUCCESS ;
			}
		}

		public override int GetSecurity ( object       fileNode ,
										  object       fileDesc ,
										  ref byte [ ] securityDescriptor )
		{
			if ( fileDesc is FileMetadata metadata )
			{
				Logger . LogTrace ( $"{nameof ( GetSecurityByName )} of {metadata . Name}" ) ;

				securityDescriptor = metadata . SecurityInfo ;

				return STATUS_SUCCESS ;
			}
			else
			{
				throw GetIoExceptionWithNtStatus ( STATUS_INVALID_HANDLE ) ;
			}
		}

		public override int SetSecurity ( object                fileNode ,
										  object                fileDesc ,
										  AccessControlSections sections ,
										  byte [ ]              securityDescriptor )
		{
			if ( fileDesc is FileMetadata metadata )
			{
				Logger . LogTrace ( $"{nameof ( SetSecurity )} for {metadata . Name}" ) ;

				FileSecurity security = new FileSecurity ( ) ;

				security . SetSecurityDescriptorBinaryForm ( metadata . SecurityInfo ) ;

				security . SetSecurityDescriptorBinaryForm ( securityDescriptor , sections ) ;

				metadata . SecurityInfo = security . GetSecurityDescriptorBinaryForm ( ) ;

				lock ( DataContext )
				{
					DataContext . SaveChanges ( ) ;
				}

				Logger . LogDebug ( $"Set security of {metadata . Name}" ) ;

				return STATUS_SUCCESS ;
			}
			else
			{
				throw GetIoExceptionWithNtStatus ( STATUS_INVALID_HANDLE ) ;
			}
		}

		#endregion

		#region Open Close

		public override int Open ( string                       fileName ,
								   uint                         createOptions ,
								   uint                         grantedAccess ,
								   out object                   fileNode ,
								   out object                   fileDesc ,
								   out Fsp . Interop . FileInfo fileInfo ,
								   out string                   normalizedName )
		{
			string normalizedFileName = fileName . Normalize ( NormalizationForm . FormD ) ;

			Logger . LogTrace ( $"{nameof ( Open )} \"{normalizedFileName}\"." ) ;

			FileMetadata metadata =
				DataContext . FileMetadata . FirstOrDefault (
															 fileMetadata
																 => fileMetadata . Name
																 == normalizedFileName ) ;

			if ( metadata != null )
			{
				if ( ( createOptions & FILE_DIRECTORY_FILE ) != 0
				  || ( ( FileAttributes ) metadata . Attributes ) . HasFlag (
																			 FileAttributes .
																				 Directory ) )
				{
					//Directory

					fileNode = null ;
				}
				else
				{
					//File

					if ( FileNodes . TryGetValue ( metadata . Guid , out FileNode node ) )
					{
						node . ReferenceCount++ ;
						node . ClosedTime = null ;
						fileNode          = node ;
					}
					else
					{
						node = new FileNode ( metadata )
							   {
								   Blocks = DataContext . BlockMetadata .
														  Where (
																 block
																	 => block . File
																	 == metadata . Guid ) .
														  OrderBy (
																   block
																	   => block . BlockSequence ) .
														  ToList ( ) ,
							   } ;

						FileNodes . Add ( metadata . Guid , node ) ;

						fileNode = node ;
					}
				}

				fileDesc = metadata ;
				fileInfo = metadata . FileInfo ;

				normalizedName = metadata . Name ;

				Logger . LogDebug ( $"Open {metadata . Name}." ) ;

				return STATUS_SUCCESS ;
			}
			else
			{
				throw GetIoExceptionWithNtStatus ( STATUS_OBJECT_NAME_NOT_FOUND ) ;
			}
		}

		public override void Close ( object fileNode , object fileDesc )
		{
			Logger . LogTrace ( $"{nameof ( Close )} \"{( fileDesc as FileMetadata ) ? . Name}." ) ;

			if ( fileNode is FileNode node )
			{
				node . Flush ( ) ;

				node . ReferenceCount-- ;

				if ( node . ReferenceCount <= 0 )
				{
					node . ClosedTime = DateTime . Now ;

					Logger . LogDebug ( $"Close {node . Metadata . Name}." ) ;
				}
				else
				{
					Logger . LogTrace (
									   $"Reduce {node . Metadata . Name} reference to {node . ReferenceCount}." ) ;
				}
			}
		}

		#endregion

		public override int Create ( string                       fileName ,
									 uint                         createOptions ,
									 uint                         grantedAccess ,
									 uint                         fileAttributes ,
									 byte [ ]                     securityDescriptor ,
									 ulong                        allocationSize ,
									 out object                   fileNode ,
									 out object                   fileDesc ,
									 out Fsp . Interop . FileInfo fileInfo ,
									 out string                   normalizedName )
		{
			string normalizedFileName =
				fileName . Normalize ( NormalizationForm . FormD ) . TrimEnd ( '\\' ) ;

			List <string> directoryDependency = normalizedFileName .
												Split (
													   '\\' ,
													   StringSplitOptions . RemoveEmptyEntries ) .
												SkipLast ( 1 ) .
												ToList ( ) ;

			StringBuilder currentDirectory = new StringBuilder ( "\\" ) ;

			foreach ( string path in directoryDependency )
			{
				currentDirectory . Append ( path ) ;

				string currentDirectoryFileName = currentDirectory . ToString ( ) ;

				FileMetadata directoryMetadata =
					DataContext . FileMetadata . SingleOrDefault (
																  fileMetadata
																	  => fileMetadata . Name
																	  == currentDirectoryFileName ) ;

				if ( directoryMetadata is null )
				{
					CreateDirectory ( currentDirectoryFileName ) ;
				}

				currentDirectory . Append ( '\\' ) ;
			}

			FileMetadata currentFile =
				DataContext . FileMetadata . SingleOrDefault (
															  ( fileMetadata )
																  => fileMetadata . Name
																  == normalizedFileName ) ;

			if ( currentFile != null )
			{
				throw GetIoExceptionWithNtStatus ( STATUS_OBJECT_NAME_COLLISION ) ;
			}

			FileMetadata metadata ;

			if ( ( createOptions & FILE_DIRECTORY_FILE ) != 0 )
			{
				//Directory
				metadata = CreateDirectory (
											normalizedFileName ,
											fileAttributes ,
											securityDescriptor ) ;

				fileNode = null ;
			}
			else
			{
				//File

				int allocatedBlockCount =
					( int ) ( ( ( long ) allocationSize + BlockSize - 1 ) / BlockSize ) ;

				metadata = CreateFile (
									   normalizedFileName ,
									   allocatedBlockCount ,
									   fileAttributes ,
									   securityDescriptor ) ;

				List <BlockMetadata> blocks = new List <BlockMetadata> ( allocatedBlockCount ) ;

				for ( int sequenceNumber = 0 ;
					  sequenceNumber < allocatedBlockCount ;
					  sequenceNumber++ )
				{
					blocks . Add ( CreateBlock ( metadata . Guid , sequenceNumber ) ) ;
				}

				FileNode node = new FileNode ( metadata ) { Blocks = blocks } ;

				FileNodes . Add ( metadata . Guid , node ) ;

				fileNode = node ;
			}

			fileDesc       = metadata ;
			fileInfo       = metadata . FileInfo ;
			normalizedName = normalizedFileName ;

			lock ( DataContext )
			{
				DataContext . SaveChanges ( ) ;
			}

			return STATUS_SUCCESS ;
		}

		public override int Flush ( object                       fileNode ,
									object                       fileDesc ,
									out Fsp . Interop . FileInfo fileInfo )
		{
			Logger . LogTrace ( $"{nameof ( Flush )} {( fileDesc as FileMetadata ) ? . Name}" ) ;

			if ( fileNode is FileNode node )
			{
				node . Flush ( ) ;
			}

			if ( fileDesc is FileMetadata metadata )
			{
				lock ( DataContext )
				{
					DataContext . SaveChanges ( ) ;
				}

				fileInfo = metadata . FileInfo ;
			}
			else
			{
				throw GetIoExceptionWithNtStatus ( STATUS_INVALID_HANDLE ) ;
			}

			return STATUS_SUCCESS ;
		}

		public override int GetVolumeInfo ( out VolumeInfo volumeInfo )
		{
			Logger . LogTrace ( $"{nameof ( GetVolumeInfo )}." ) ;

			volumeInfo = VolumeInfo ;
			return STATUS_SUCCESS ;
		}

		public override int SetVolumeLabel ( string volumeLabel , out VolumeInfo volumeInfo )
		{
			Logger . LogTrace ( $"{nameof ( SetVolumeLabel )} to {volumeLabel}." ) ;

			Program . Current . Setting . VolumeLabel = volumeLabel ;
			volumeInfo                                = VolumeInfo ;

			Program . Current . SaveSettingFile ( ) ;

			return STATUS_SUCCESS ;
		}

		public override int Rename ( object fileNode ,
									 object fileDesc ,
									 string fileName ,
									 string newFileName ,
									 bool   replaceIfExists )
		{
			if ( fileNode is FileNode node
			  && fileDesc is FileMetadata metadata )
			{
				Logger . LogInformation (
										 $"{nameof ( Rename )} \"{metadata . Name}\" to {newFileName}." ) ;

				string normalizedNewFileName =
					newFileName . Normalize ( NormalizationForm . FormD ) ;

				FileMetadata previousFileMetadata =
					DataContext . FileMetadata . FirstOrDefault (
																 fileMetadata
																	 => fileMetadata . Name
																	 == normalizedNewFileName ) ;


				if ( previousFileMetadata != null )
				{
					if ( replaceIfExists )
					{
						previousFileMetadata . Name =
							$"Replaced_{previousFileMetadata . Guid}_{previousFileMetadata . Name}" ;
					}
					else
					{
						throw GetIoExceptionWithNtStatus ( STATUS_OBJECT_NAME_COLLISION ) ;
					}
				}

				if ( ( ( FileAttributes ) metadata . Attributes ) . HasFlag (
																			 FileAttributes .
																				 Directory ) )
				{
					lock ( DataContext )
					{
						DataContext . FileMetadata .
									  Where ( ( fileMetadata ) => fileMetadata . Name . StartsWith ( metadata . Name ) ) .
									  ForEachAsync ( ( fileMetadata ) => fileMetadata . Name = fileMetadata . Name . Replace ( metadata . Name , normalizedNewFileName ) ) ;
					}
				}
				else
				{
					metadata . Name = normalizedNewFileName ;
				}

				lock ( DataContext )
				{
					DataContext . SaveChanges ( ) ;
				}

				return STATUS_SUCCESS ;
			}

			throw GetIoExceptionWithNtStatus ( STATUS_INVALID_HANDLE ) ;
		}

		public override int Overwrite ( object                       fileNode ,
										object                       fileDesc ,
										uint                         fileAttributes ,
										bool                         replaceFileAttributes ,
										ulong                        allocationSize ,
										out Fsp . Interop . FileInfo fileInfo )
		{
			if ( fileNode is FileNode node
			  && fileDesc is FileMetadata metadata )
			{
				Logger . LogTrace ( $"{nameof ( Overwrite )} of \"{metadata . Name}\"." ) ;

				metadata . Size = 0 ;
				if ( replaceFileAttributes )
				{
					metadata . Attributes = fileAttributes ;
				}
				else
				{
					metadata . Attributes |= fileAttributes ;
				}

				lock ( DataContext )
				{
					DataContext . SaveChanges ( ) ;
				}

				fileInfo = metadata . FileInfo ;

				return STATUS_SUCCESS ;
			}

			throw GetIoExceptionWithNtStatus ( STATUS_INVALID_HANDLE ) ;
		}

		public override int Read ( object   fileNode ,
								   object   fileDesc ,
								   IntPtr   buffer ,
								   ulong    offset ,
								   uint     length ,
								   out uint bytesTransferred )
		{
			if ( fileNode is FileNode node
			  && fileDesc is FileMetadata metadata )
			{
				Logger . LogTrace (
								   $"Request {nameof ( Read )} from \"{metadata . Name}\", start from {offset} with length {length}({( ( long ) length ) . BytesCountToHumanString ( )})." ) ;

				if ( offset >= ( ulong ) metadata . Size )
				{
					bytesTransferred = default ;
					return STATUS_END_OF_FILE ;
				}

				length = ( uint ) Math . Min ( length , metadata . Size - ( long ) offset ) ;

				int startBlockSequence = ( int ) ( offset / ( ulong ) BlockSize ) ;

				int endBlockSequence =
					( int ) ( ( offset + length + ( ulong ) BlockSize - 1 )
							/ ( ulong ) BlockSize ) ;

				endBlockSequence = Math . Min (
											   endBlockSequence ,
											   metadata . AllocatedBlockCount - 1 ) ;

				int currentByteSequence = 0 ;

				int currentBlockSequence = startBlockSequence ;

				#region firstBlock

				CachedBlock currentBlock = node . GetBlock ( currentBlockSequence ) ;

				int firstByteStartFrom = ( int ) ( offset % ( ulong ) BlockSize ) ;

				int currentBlockCopyByteCount = ( int ) Math . Min (
																	BlockSize - firstByteStartFrom ,
																	length
																  - currentByteSequence ) ;

				Marshal . Copy (
								currentBlock . Content ,
								firstByteStartFrom ,
								buffer + currentByteSequence ,
								currentBlockCopyByteCount ) ;

				currentByteSequence += currentBlockCopyByteCount ;

				currentBlockSequence++ ;

				#endregion

				firstByteStartFrom = 0 ;

				for ( ; currentBlockSequence < endBlockSequence ; currentBlockSequence++ )
				{
					currentBlock = node . GetBlock ( currentBlockSequence ) ;


					currentBlockCopyByteCount = ( int ) Math . Min (
																	BlockSize - firstByteStartFrom ,
																	length
																  - currentByteSequence ) ;

					Marshal . Copy (
									currentBlock . Content ,
									firstByteStartFrom ,
									buffer + currentByteSequence ,
									currentBlockCopyByteCount ) ;

					currentByteSequence += currentBlockCopyByteCount ;
				}

				bytesTransferred = ( uint ) currentByteSequence ;

				Logger . LogInformation (
										 $"Read from \"{metadata . Name}\", start from {offset} with length {bytesTransferred}({( ( long ) bytesTransferred ) . BytesCountToHumanString ( )})." ) ;

				return STATUS_SUCCESS ;
			}

			throw GetIoExceptionWithNtStatus ( STATUS_INVALID_HANDLE ) ;
		}

		public override int Write ( object                       fileNode ,
									object                       fileDesc ,
									IntPtr                       buffer ,
									ulong                        offset ,
									uint                         length ,
									bool                         writeToEndOfFile ,
									bool                         constrainedIo ,
									out uint                     bytesTransferred ,
									out Fsp . Interop . FileInfo fileInfo )
		{
			if ( fileNode is FileNode node
			  && fileDesc is FileMetadata metadata )
			{
				Logger . LogTrace (
								   $"Request {nameof ( Write )} to \"{metadata . Name}\", start from {offset} with length {length}({( ( long ) length ) . BytesCountToHumanString ( )})." ) ;

				if ( writeToEndOfFile )
				{
					offset = ( ulong ) metadata . Size ;
				}

				if ( ! constrainedIo )
				{
					ResizeFile (
								node ,
								Math . Max ( metadata . Size , ( long ) offset + length ) ) ;
					length = ( uint ) Math . Min ( metadata . Size - ( long ) offset , length ) ;
				}

				int startBlockSequence = ( int ) ( offset / ( ulong ) BlockSize ) ;

				int endBlockSequence =
					( int ) ( ( offset + length + ( ulong ) BlockSize - 1 )
							/ ( ulong ) BlockSize ) ;

				int currentByteSequence = 0 ;

				int currentBlockSequence = startBlockSequence ;

				#region firstBlock

				CachedBlock currentBlock = node . GetBlock ( currentBlockSequence ) ;

				int firstByteStartFrom = ( int ) ( offset % ( ulong ) BlockSize ) ;

				int currentBlockCopyByteCount = ( int ) Math . Min (
																	BlockSize - firstByteStartFrom ,
																	length
																  - currentByteSequence ) ;

				currentBlock . IsModified = true ;

				Marshal . Copy (
								buffer + currentByteSequence ,
								currentBlock . Content ,
								firstByteStartFrom ,
								currentBlockCopyByteCount ) ;

				currentByteSequence += currentBlockCopyByteCount ;

				currentBlockSequence++ ;

				#endregion

				firstByteStartFrom = 0 ;

				for ( ; currentBlockSequence < endBlockSequence ; currentBlockSequence++ )
				{
					currentBlock = node . GetBlock ( currentBlockSequence ) ;

					currentBlockCopyByteCount = ( int ) Math . Min (
																	BlockSize - firstByteStartFrom ,
																	length
																  - currentByteSequence ) ;


					currentBlock . IsModified = true ;

					Marshal . Copy (
									buffer + currentByteSequence ,
									currentBlock . Content ,
									firstByteStartFrom ,
									currentBlockCopyByteCount ) ;

					currentByteSequence += currentBlockCopyByteCount ;
				}

				bytesTransferred = ( uint ) currentByteSequence ;

				fileInfo = metadata . FileInfo ;

				Logger . LogInformation (
										 $"Written to \"{metadata . Name}\", start from {offset} with length {bytesTransferred}({( ( long ) bytesTransferred ) . BytesCountToHumanString ( )})." ) ;

				return STATUS_SUCCESS ;
			}

			throw GetIoExceptionWithNtStatus ( STATUS_INVALID_HANDLE ) ;
		}

		public override bool ReadDirectoryEntry ( object                       fileNode ,
												  object                       fileDesc ,
												  string                       pattern ,
												  string                       marker ,
												  ref object                   context ,
												  out string                   fileName ,
												  out Fsp . Interop . FileInfo fileInfo )
		{
			if ( fileDesc is FileMetadata metadata )
			{
				if ( ! ( context is EnumerateDirectoryContext currentContext ) )
				{
					Logger . LogTrace (
									   $"{nameof ( ReadDirectoryEntry )} of \"{metadata . Name}\"." ) ;

					if ( null != pattern )
					{
						pattern = pattern . Replace ( '<' , '*' ) .
											Replace ( '>' , '?' ) .
											Replace ( '"' , '.' ) ;
					}
					else
					{
						pattern = "*" ;
					}

					Regex fileNamePatternRegex = FindFilesPatternToRegex . Convert ( pattern ) ;

					string pathPrefix ;

					if ( metadata . Name . EndsWith ( "\\" ) )
					{
						pathPrefix = metadata . Name ;
					}
					else
					{
						pathPrefix = $"{metadata . Name}\\" ;
					}

					string escapedPathPrefix = Regex . Escape ( pathPrefix ) ;
					Regex pathPrefixRegex = new Regex (
													   @$"^{escapedPathPrefix}([^\\]+)$" ,
													   RegexOptions . Compiled ) ;


					IEnumerable <FileMetadata> fileInFolder = DataContext .
															  FileMetadata .
															  Where (
																	 fileMetadata
																		 => fileMetadata .
																			Name . StartsWith (
																							   pathPrefix ) ) .
															  OrderBy (
																	   fileMetadata
																		   => fileMetadata .
																			   Name ) .
															  AsEnumerable ( ) ;

					List <(string FileName , FileMetadata FileMetadata)> availableFiles =
						fileInFolder . Select (
											   fileMetadata =>
											   {
												   Match pathPrefixMatch =
													   pathPrefixRegex . Match (
																				fileMetadata .
																					Name ) ;

												   if ( pathPrefixMatch . Success )
												   {
													   string matchedFileName = pathPrefixMatch .
																				Groups [ 1 ] .
																				Value ;


													   Match fileNameMatch =
														   fileNamePatternRegex . Match (
																						 matchedFileName ) ;

													   if ( fileNameMatch . Success )
													   {
														   return ( FileName : matchedFileName ,
																	FileMetadata : fileMetadata ) ;
													   }
												   }

												   return default ;
											   } ) .
									   Where ( file => file . FileName != null ) .
									   ToList ( ) ;

					availableFiles . Add ( ( "." , metadata ) ) ;

					int progress = - 1 ;

					if ( marker != null )
					{
						progress =
							availableFiles . FindIndex ( ( file ) => file . FileName == marker ) ;
					}

					currentContext = new EnumerateDirectoryContext ( )
									 {
										 AvailableFiles = availableFiles , Progress = progress
									 } ;

					Logger . LogDebug ( $"Read Directory {pathPrefix}\\ Content." ) ;
				}

				currentContext . Progress++ ;
				context = currentContext ;

				if ( currentContext . AvailableFiles . Count > currentContext . Progress )
				{
					(string FileName , FileMetadata FileMetadata) currentFile =
						currentContext . AvailableFiles [ currentContext . Progress ] ;
					fileName = currentFile . FileName ;
					fileInfo = currentFile . FileMetadata . FileInfo ;


					return true ;
				}
				else
				{
					fileName = default ;
					fileInfo = default ;
					return false ;
				}
			}
			else
			{
				throw GetIoExceptionWithNtStatus ( STATUS_INVALID_HANDLE ) ;
			}
		}

		public override int SetBasicInfo ( object                       fileNode ,
										   object                       fileDesc ,
										   uint                         fileAttributes ,
										   ulong                        creationTime ,
										   ulong                        lastAccessTime ,
										   ulong                        lastWriteTime ,
										   ulong                        changeTime ,
										   out Fsp . Interop . FileInfo fileInfo )
		{
			if ( fileDesc is FileMetadata metadata )
			{
				Logger . LogTrace ( $"{nameof ( SetBasicInfo )} for {metadata . Name}" ) ;

				metadata . Attributes     = fileAttributes ;
				metadata . CreationTime   = creationTime ;
				metadata . LastAccessTime = lastAccessTime ;
				metadata . LastWriteTime  = lastAccessTime ;
				metadata . ChangeTime     = changeTime ;

				lock ( DataContext )
				{
					DataContext . SaveChanges ( ) ;
				}

				fileInfo = metadata . FileInfo ;

				Logger . LogDebug ( $"Set info of {metadata . Name}" ) ;

				return STATUS_SUCCESS ;
			}
			else
			{
				throw GetIoExceptionWithNtStatus ( STATUS_INVALID_HANDLE ) ;
			}
		}

		public override int SetFileSize ( object                       fileNode ,
										  object                       fileDesc ,
										  ulong                        newSize ,
										  bool                         setAllocationSize ,
										  out Fsp . Interop . FileInfo fileInfo )
		{
			if ( fileNode is FileNode node
			  && fileDesc is FileMetadata metadata )
			{
				Logger . LogTrace (
								   $"{nameof ( SetFileSize )} of \"{metadata . Name}\", from {metadata . Size}({metadata . Size . BytesCountToHumanString ( )}) to {newSize}({( ( long ) newSize ) . BytesCountToHumanString ( )})." ) ;

				ResizeFile ( node , ( long ) newSize ) ;

				lock ( DataContext )
				{
					DataContext . SaveChanges ( ) ;
				}

				fileInfo = metadata . FileInfo ;

				return STATUS_SUCCESS ;
			}

			throw GetIoExceptionWithNtStatus ( STATUS_INVALID_HANDLE ) ;
		}

		public override int GetFileInfo ( object                       fileNode ,
										  object                       fileDesc ,
										  out Fsp . Interop . FileInfo fileInfo )
		{
			if ( fileDesc is FileMetadata metadata )
			{
				fileInfo = metadata . FileInfo ;

				return STATUS_SUCCESS ;
			}
			else
			{
				throw GetIoExceptionWithNtStatus ( STATUS_INVALID_HANDLE ) ;
			}
		}

		#region Mount Unmount

		public override int Mounted ( object Host ) { return STATUS_SUCCESS ; }

		public override void Unmounted ( object host )
		{
			lock ( DataContext )
			{
				DataContext . SaveChanges ( ) ;
			}

			lock ( FileNodes )
			{
				foreach ( KeyValuePair <Guid , FileNode> fileNodePair in FileNodes )
				{
					fileNodePair . Value . Flush ( ) ;
				}

				FileNodes . Clear ( ) ;
			}
		}

		#endregion

		//public override int SetDelete(object FileNode, object FileDesc, string FileName, bool DeleteFile) => base.SetDelete(FileNode, FileDesc, FileName, DeleteFile);


		public CloudFileSystem ( )
		{
			Logger ??= StaticServiceProvider . Provider . GetService <ILoggerFactory> ( ) .
											   CreateLogger <CloudFileSystem> ( ) ;

			DataContext = new DataContext ( ) ;

			Logger . LogInformation ( "Database Initialized." ) ;
		}

		public static IOException GetIoExceptionWithHResult ( int hResult )
			=> new IOException ( null , hResult ) ;

		public static IOException GetIoExceptionWithWin32 ( int error )
			=> GetIoExceptionWithHResult ( unchecked ( ( int ) ( 0x80070000 | error ) ) ) ;

		public static IOException GetIoExceptionWithNtStatus ( int status )
			=> GetIoExceptionWithWin32 ( ( int ) Win32FromNtStatus ( status ) ) ;

		public override int ExceptionHandler ( Exception ex )
		{
			int hResult = ex . HResult ; /* needs Framework 4.5 */
			if ( 0x80070000 == ( hResult & 0xFFFF0000 ) )
			{
				return NtStatusFromWin32 ( ( uint ) hResult & 0xFFFF ) ;
			}

			return STATUS_UNEXPECTED_IO_ERROR ;
		}

	}

}
