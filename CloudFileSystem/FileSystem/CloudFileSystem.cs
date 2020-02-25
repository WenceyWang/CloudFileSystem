using System ;
using System . Collections ;
using System . Collections . Generic ;
using System . IO ;
using System . Linq ;
using System . Runtime . InteropServices ;
using System . Security . AccessControl ;
using System . Text ;
using System . Text . RegularExpressions ;

using DreamRecorder . CloudFileSystem . PageBlobProviders ;
using DreamRecorder . CloudFileSystem . Program ;
using DreamRecorder . ToolBox . General ;

using Fsp ;
using Fsp . Interop ;

using JetBrains . Annotations ;

using Microsoft . Extensions . DependencyInjection ;
using Microsoft . Extensions . Logging ;

using FileInfo = Fsp . Interop . FileInfo ;

namespace DreamRecorder . CloudFileSystem . FileSystem
{

	public class CloudFileSystem : FileSystemBase
	{

		public ILogger Logger { get ; }

		public static int BlockSize => 2 * 1024 * 1024 ;

		public ProgramSetting Setting => Program . Program . Current . Setting ;

		public static CloudFileSystem Current { get ; private set ; }

		public DataContext DataContext { get ; set ; }

		public Dictionary <Guid , FileNode> FileNodes { get ; set ; } =
			new Dictionary <Guid , FileNode> ( ) ;

		public VolumeInfo VolumeInfo
		{
			get
			{
				VolumeInfo volumeInfo = new VolumeInfo ( ) { } ;
				volumeInfo . SetVolumeLabel ( Setting . VolumeLabel ) ;

				volumeInfo . TotalSize = ( ulong ) ( Quota ? . TotalQuota     ?? long . MaxValue ) ;
				volumeInfo . FreeSize  = ( ulong ) ( Quota ? . RemainingQuota ?? 0 ) ;

				return volumeInfo ;
			}
		}

		public ITaskDispatcher TaskDispatcher => Program . Program . TaskDispatcher ;

		public IPageBlobProvider PageBlobProvider { get ; } =
			StaticServiceProvider . Provider . GetService <IPageBlobProvider> ( ) ;

		public Quota ? Quota { get ; set ; }

		public CloudFileSystem ( )
		{
			Logger ??= StaticServiceProvider . Provider . GetService <ILoggerFactory> ( ) .
											   CreateLogger <CloudFileSystem> ( ) ;

			DataContext = new DataContext ( ) ;

			Logger . LogInformation ( "Database Initialized." ) ;
		}

		public override void Cleanup ( object fileNode ,
									   object fileDesc ,
									   string fileName ,
									   uint   flags )
		{
			if ( fileDesc is FileMetadata metadata )
			{
				Logger . LogTrace ( $"{nameof ( Cleanup )} \"{metadata . Name}\"." ) ;

				if ( 0 != ( flags & CleanupDelete ) )
				{
					metadata . IsDeleted = true ;
				}

				FlushMetadata ( ) ;
			}
			else
			{
				throw GetIoExceptionWithNtStatus ( STATUS_INVALID_HANDLE ) ;
			}
		}

		public override int CanDelete ( object fileNode , object fileDesc , string fileName )
		{
			if ( fileDesc is FileMetadata metadata )
			{
				if ( ( ( FileAttributes ) metadata . FileInfo . FileAttributes ) . HasFlag (
																							FileAttributes .
																								Directory )
				)
				{
					string pathPrefix ;

					if ( metadata . Name . EndsWith ( "\\" ) )
					{
						pathPrefix = metadata . Name ;
					}
					else
					{
						pathPrefix = $"{metadata . Name}\\" ;
					}

					lock ( DataContext )
					{
						if ( DataContext .
							 FileMetadata .
							 Where ( ( fileMetadata ) => ! fileMetadata . IsDeleted ) .
							 Any (
								  ( fileMetadata )
									  => fileMetadata . Name . StartsWith ( pathPrefix ) ) )
						{
							return STATUS_DIRECTORY_NOT_EMPTY ;
						}
						else
						{
							return STATUS_SUCCESS ;
						}
					}
				}
				else
				{
					return STATUS_SUCCESS ;
				}
			}
			else
			{
				throw GetIoExceptionWithNtStatus ( STATUS_INVALID_HANDLE ) ;
			}
		}

		public void UpdateQuota ( )
		{
			Logger . LogTrace ( "Update Quota" ) ;

			Quota = PageBlobProvider . GetQuota ( ) ;
		}

		public void ResizeFile ( [NotNull] FileNode file , long newSize )
		{
			if ( file == null )
			{
				throw new ArgumentNullException ( nameof ( file ) ) ;
			}

			if ( file . Metadata . Size == newSize )
			{
				return ;
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

			FlushMetadata ( ) ;
		}

		public override int Init ( object host )
		{
			Logger . LogTrace ( $"{nameof ( Init )}" ) ;

			Current = this ;

			UpdateQuota ( ) ;

			FileMetadata rootDirectoryMetadata ;
			lock ( DataContext )
			{
				rootDirectoryMetadata =
					DataContext . FileMetadata . SingleOrDefault (
																  metadata
																	  => metadata . Name == "\\" ) ;
			}

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

			IntervalTask updateQuotaTask = new IntervalTask (
															 UpdateQuota ,
															 TimeSpan . FromMinutes ( 2 ) ,
															 priority : TaskPriority .
																 Background ) ;

			IntervalTask flushMetadataTask = new IntervalTask (
															   FlushMetadata ,
															   TimeSpan . FromSeconds ( 20 ) ,
															   priority : TaskPriority . Low ) ;

			TaskDispatcher . Dispatch ( updateQuotaTask ) ;
			TaskDispatcher . Dispatch ( flushMetadataTask ) ;

			return STATUS_SUCCESS ;
		}

		public override int Create ( string       fileName ,
									 uint         createOptions ,
									 uint         grantedAccess ,
									 uint         fileAttributes ,
									 byte [ ]     securityDescriptor ,
									 ulong        allocationSize ,
									 out object   fileNode ,
									 out object   fileDesc ,
									 out FileInfo fileInfo ,
									 out string   normalizedName )
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

				FileMetadata directoryMetadata ;
				lock ( DataContext )
				{
					directoryMetadata = DataContext . FileMetadata . SingleOrDefault (
																					  fileMetadata
																						  => fileMetadata .
																								 Name
																						  == currentDirectoryFileName ) ;
				}

				if ( directoryMetadata is null )
				{
					CreateDirectory ( currentDirectoryFileName ) ;
				}

				currentDirectory . Append ( '\\' ) ;
			}

			FileMetadata metadata ;
			lock ( DataContext )
			{
				metadata = DataContext . FileMetadata . SingleOrDefault (
																		 ( fileMetadata )
																			 => fileMetadata . Name
																			 == normalizedFileName ) ;
			}

			if ( metadata != null )
			{
				if ( ( createOptions & FILE_DIRECTORY_FILE ) != 0 )
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
						node = new FileNode ( metadata ) ;
						lock ( DataContext )
						{
							node . Blocks = DataContext . BlockMetadata .
														  Where (
																 block
																	 => block . File
																	 == metadata . Guid ) .
														  OrderBy (
																   block
																	   => block . BlockSequence ) .
														  ToList ( ) ;
						}

						lock ( FileNodes )
						{
							FileNodes . Add ( metadata . Guid , node ) ;
						}

						fileNode = node ;
					}
				}
			}
			else
			{
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

					FlushMetadata ( ) ;
				}
			}

			fileDesc       = metadata ;
			fileInfo       = metadata . FileInfo ;
			normalizedName = normalizedFileName ;

			return STATUS_SUCCESS ;
		}

		public void FlushMetadata ( )
		{
			Logger . LogTrace ( $"{nameof ( FlushMetadata )}" ) ;

			lock ( DataContext )
			{
				DataContext . SaveChanges ( ) ;
			}
		}

		public override int Flush ( object fileNode , object fileDesc , out FileInfo fileInfo )
		{
			Logger . LogTrace ( $"{nameof ( Flush )} {( fileDesc as FileMetadata ) ? . Name}" ) ;

			if ( fileNode is FileNode node )
			{
				OnetimeTask flushTask = new OnetimeTask ( node . Flush , default ) ;
				TaskDispatcher . Dispatch ( flushTask ) ;
			}

			FlushMetadata ( ) ;

			if ( fileDesc is FileMetadata metadata )
			{
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

			Program . Program . Current . Setting . VolumeLabel = volumeLabel ;
			volumeInfo                                          = VolumeInfo ;

			Program . Program . Current . SaveSettingFile ( ) ;

			return STATUS_SUCCESS ;
		}

		public override int Rename ( object fileNode ,
									 object fileDesc ,
									 string fileName ,
									 string newFileName ,
									 bool   replaceIfExists )
		{
			if ( fileDesc is FileMetadata metadata )
			{
				Logger . LogDebug (
								   $"{nameof ( Rename )} \"{metadata . Name}\" to {newFileName}." ) ;

				string normalizedNewFileName =
					newFileName . Normalize ( NormalizationForm . FormD ) ;

				FileMetadata previousFileMetadata ;
				lock ( DataContext )
				{
					previousFileMetadata = DataContext . FileMetadata . FirstOrDefault (
																						fileMetadata
																							=> fileMetadata .
																								   Name
																							== normalizedNewFileName ) ;
				}

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
					IEnumerable <FileMetadata> filesInFolder ;

					lock ( DataContext )
					{
						filesInFolder = DataContext . FileMetadata .
													  Where (
															 ( fileMetadata )
																 => fileMetadata .
																	Name . StartsWith (
																					   metadata .
																						   Name ) ) .
													  AsEnumerable ( ) ;
					}

					string previousName = metadata . Name ;

					foreach ( FileMetadata fileMetadata in filesInFolder )
					{
						fileMetadata . Name =
							fileMetadata . Name . Replace ( previousName , normalizedNewFileName ) ;
					}
				}
				else
				{
					metadata . Name = normalizedNewFileName ;
				}

				FlushMetadata ( ) ;

				return STATUS_SUCCESS ;
			}

			throw GetIoExceptionWithNtStatus ( STATUS_INVALID_HANDLE ) ;
		}

		public override int Overwrite ( object       fileNode ,
										object       fileDesc ,
										uint         fileAttributes ,
										bool         replaceFileAttributes ,
										ulong        allocationSize ,
										out FileInfo fileInfo )
		{
			if ( fileNode is FileNode node
			  && fileDesc is FileMetadata metadata )
			{
				Logger . LogTrace ( $"{nameof ( Overwrite )} \"{metadata . Name}\"." ) ;

				metadata . Size = 0 ;
				if ( replaceFileAttributes )
				{
					metadata . Attributes = fileAttributes ;
				}
				else
				{
					metadata . Attributes |= fileAttributes ;
				}

				FlushMetadata ( ) ;

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

				Logger . LogDebug (
								   $"Read from \"{metadata . Name}\", start from {offset} with length {bytesTransferred}({( ( long ) bytesTransferred ) . BytesCountToHumanString ( )})." ) ;

				return STATUS_SUCCESS ;
			}

			throw GetIoExceptionWithNtStatus ( STATUS_INVALID_HANDLE ) ;
		}

		public override int Write ( object       fileNode ,
									object       fileDesc ,
									IntPtr       buffer ,
									ulong        offset ,
									uint         length ,
									bool         writeToEndOfFile ,
									bool         constrainedIo ,
									out uint     bytesTransferred ,
									out FileInfo fileInfo )
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

				CachedBlock currentBlock = node . GetBlock ( currentBlockSequence ) ;

				#region firstBlock

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

				Logger . LogDebug (
								   $"Written to \"{metadata . Name}\", start from {offset} with length {bytesTransferred}({( ( long ) bytesTransferred ) . BytesCountToHumanString ( )})." ) ;

				return STATUS_SUCCESS ;
			}

			throw GetIoExceptionWithNtStatus ( STATUS_INVALID_HANDLE ) ;
		}

		public override bool ReadDirectoryEntry ( object       fileNode ,
												  object       fileDesc ,
												  string       pattern ,
												  string       marker ,
												  ref object   context ,
												  out string   fileName ,
												  out FileInfo fileInfo )
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

					IEnumerable <FileMetadata> fileInFolder ;
					lock ( DataContext )
					{
						fileInFolder = DataContext .
									   FileMetadata .
									   Where ( ( fileMetadata ) => ! fileMetadata . IsDeleted ) .
									   Where (
											  fileMetadata
												  => fileMetadata .
													 Name . StartsWith ( pathPrefix ) ) .
									   OrderBy ( fileMetadata => fileMetadata . Name ) .
									   AsEnumerable ( ) ;
					}

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

		public override int SetBasicInfo ( object       fileNode ,
										   object       fileDesc ,
										   uint         fileAttributes ,
										   ulong        creationTime ,
										   ulong        lastAccessTime ,
										   ulong        lastWriteTime ,
										   ulong        changeTime ,
										   out FileInfo fileInfo )
		{
			if ( fileDesc is FileMetadata metadata )
			{
				Logger . LogTrace ( $"{nameof ( SetBasicInfo )} for {metadata . Name}" ) ;

				if ( fileAttributes != uint . MaxValue )
				{
					metadata . Attributes = fileAttributes ;
				}

				if ( creationTime != 0 )
				{
					metadata . CreationTime = creationTime ;
				}

				if ( lastAccessTime != 0 )
				{
					metadata . LastAccessTime = lastAccessTime ;
				}

				if ( lastWriteTime != 0 )
				{
					metadata . LastWriteTime = lastWriteTime ;
				}

				if ( changeTime != 0 )
				{
					metadata . ChangeTime = changeTime ;
				}

				FlushMetadata ( ) ;

				fileInfo = metadata . FileInfo ;

				Logger . LogDebug ( $"Set info of {metadata . Name}" ) ;

				return STATUS_SUCCESS ;
			}
			else
			{
				throw GetIoExceptionWithNtStatus ( STATUS_INVALID_HANDLE ) ;
			}
		}

		public override int SetFileSize ( object       fileNode ,
										  object       fileDesc ,
										  ulong        newSize ,
										  bool         setAllocationSize ,
										  out FileInfo fileInfo )
		{
			if ( fileNode is FileNode node
			  && fileDesc is FileMetadata metadata )
			{
				Logger . LogTrace (
								   $"{nameof ( SetFileSize )} of \"{metadata . Name}\", from {metadata . Size}({metadata . Size . BytesCountToHumanString ( )}) to {newSize}({( ( long ) newSize ) . BytesCountToHumanString ( )})." ) ;

				ResizeFile ( node , ( long ) newSize ) ;

				FlushMetadata ( ) ;

				fileInfo = metadata . FileInfo ;

				return STATUS_SUCCESS ;
			}

			throw GetIoExceptionWithNtStatus ( STATUS_INVALID_HANDLE ) ;
		}

		public override int GetFileInfo ( object       fileNode ,
										  object       fileDesc ,
										  out FileInfo fileInfo )
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
										securityDescriptor ?? DefaultSecurity . RootSecurity ,
									Size      = 0 ,
									IsDeleted = false ,
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
										securityDescriptor ?? DefaultSecurity . RootSecurity ,
									Size      = 0 ,
									IsDeleted = false ,
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
											  File          = file ,
											  BlockSequence = sequence ,
											  IsEncrypted   = Setting . EncryptFileBlock ,
										  } ;

			lock ( DataContext )
			{
				DataContext . BlockMetadata . Add ( blockMetadata ) ;

				DataContext . SaveChanges ( ) ;
			}

			return blockMetadata ;
		}

		#endregion

		#region Security

		public override int GetSecurityByName ( string       fileName ,
												out uint     fileAttributes ,
												ref byte [ ] securityDescriptor )
		{
			Logger . LogTrace ( $"{nameof ( GetSecurityByName )} of {fileName}" ) ;

			FileMetadata fileMetadata = null ;

			lock ( DataContext )
			{
				fileMetadata =
					DataContext . FileMetadata . SingleOrDefault (
																  file
																	  => file . Name == fileName ) ;
			}

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

				if ( sections != AccessControlSections . None )
				{
					security . SetSecurityDescriptorBinaryForm ( metadata . SecurityInfo ) ;

					security . SetSecurityDescriptorBinaryForm ( securityDescriptor , sections ) ;

					metadata . SecurityInfo = security . GetSecurityDescriptorBinaryForm ( ) ;
				}
				else
				{
					metadata . SecurityInfo = securityDescriptor ;
				}

				FlushMetadata ( ) ;

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

		public override int Open ( string       fileName ,
								   uint         createOptions ,
								   uint         grantedAccess ,
								   out object   fileNode ,
								   out object   fileDesc ,
								   out FileInfo fileInfo ,
								   out string   normalizedName )
		{
			string normalizedFileName = fileName . Normalize ( NormalizationForm . FormD ) ;

			Logger . LogTrace ( $"{nameof ( Open )} \"{normalizedFileName}\"." ) ;

			FileMetadata metadata ;
			lock ( DataContext )
			{
				metadata = DataContext . FileMetadata . FirstOrDefault (
																		fileMetadata
																			=> fileMetadata . Name
																			== normalizedFileName ) ;
			}

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
						node = new FileNode ( metadata ) ;
						lock ( DataContext )
						{
							node . Blocks = DataContext . BlockMetadata .
														  Where (
																 block
																	 => block . File
																	 == metadata . Guid ) .
														  OrderBy (
																   block
																	   => block . BlockSequence ) .
														  ToList ( ) ;
						}

						lock ( FileNodes )
						{
							FileNodes . Add ( metadata . Guid , node ) ;
						}

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
				node . ReferenceCount-- ;

				if ( node . ReferenceCount <= 0 )
				{
					node . ClosedTime = DateTime . Now ;

					OnetimeTask flushTask = new OnetimeTask ( node . Flush , default ) ;
					TaskDispatcher . Dispatch ( flushTask ) ;

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

		#region Mount Unmount

		public override int Mounted ( object Host ) => STATUS_SUCCESS ;

		public override void Unmounted ( object host )
		{
			FlushMetadata ( ) ;

			lock ( FileNodes )
			{
				foreach ( KeyValuePair <Guid , FileNode> fileNodePair in FileNodes )
				{
					bool success = false ;

					while ( ! success )
					{
						try
						{
							fileNodePair . Value . Flush ( ) ;
							success = true ;
						}
						catch ( Exception e )
						{
							Console . WriteLine ( e ) ;
						}
					}
				}

				FileNodes . Clear ( ) ;
			}
		}

		#endregion

	}

}
