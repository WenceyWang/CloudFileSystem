using System ;
using System . Collections ;
using System . Collections . Generic ;
using System . IO ;
using System . Linq ;
using System . Threading . Tasks ;

using Microsoft . Graph ;

namespace DreamRecorder . CloudFileSystem
{

	public class FileNode
	{

		public int ReferenceCount = 1 ;

		public DateTime ? ClosedTime { get ; set ; }

		public FileMetadata Metadata { get ; set ; }

		public List <BlockMetadata> Blocks { get ; set ; } = new List <BlockMetadata> ( ) ;

		public SortedList <long , CachedBlock> CachedBlocks { get ; set ; } =
			new SortedList <long , CachedBlock> ( ) ;

		public GraphServiceClient GraphServiceClient
			=> CloudFileSystem . Current . GraphServiceClient ;

		public long BlockSize => CloudFileSystem . BlockSize ;

		public FileNode ( FileMetadata fileMetadata ) => Metadata = fileMetadata ;

		public void Flush ( )
		{
			DataContext dataContext = CloudFileSystem . Current . DataContext ;

			lock ( dataContext )
			{
				lock ( CachedBlocks )
				{
					List <Task> tasks = new List <Task> ( ) { dataContext . SaveChangesAsync ( ) } ;

					foreach ( KeyValuePair <long , CachedBlock> cachedPair in CachedBlocks )
					{
						CachedBlock cachedBlock = cachedPair . Value ;
						tasks . Add ( UpdateRemoteBlock ( cachedBlock ) ) ;
					}

					Task . WaitAll ( tasks . ToArray ( ) ) ;
				}
			}
		}

		public async Task UpdateRemoteBlock ( CachedBlock cachedBlock )
		{
			if ( cachedBlock . IsModified )
			{
				long sequence = cachedBlock . Sequence ;

				MemoryStream dataStream = new MemoryStream ( cachedBlock . Content ) ;

				string fileName =
					$"{Program . Current . Setting . BlockDirectory}\\{Metadata . Guid}_{sequence}.bin" ;

				DriveItem remoteFile = await GraphServiceClient .
											 Me . Drive . Root . ItemWithPath ( fileName ) .
											 Content . Request ( ) .
											 WithMaxRetry ( 5 ) .
											 PutAsync <DriveItem> ( dataStream ) ;

				cachedBlock . Metadata . RemoteFileId = remoteFile . Id ;
				cachedBlock . IsModified              = false ;
			}
		}

		public async Task <byte [ ]> DownloadRemoteBlock ( long sequence )
		{
			string fileName =
				$"{Program . Current . Setting . BlockDirectory}\\{Metadata . Guid}_{sequence}.bin" ;

			Stream remoteFile = null ;

			try
			{
				remoteFile = await GraphServiceClient .
								   Me . Drive . Root . ItemWithPath ( fileName ) .
								   Content . Request ( ) .
								   GetAsync ( ) ;
			}
			catch ( Exception e )
			{
				Console . WriteLine ( e ) ;
			}

			byte [ ] blockData = new byte[ BlockSize ] ;

			if ( remoteFile != null )
			{
				await remoteFile . ReadAsync ( blockData ) ;
			}

			return blockData ;
		}

		public CachedBlock GetBlock ( int sequenceNumber )
		{
			if ( CachedBlocks . TryGetValue ( sequenceNumber , out CachedBlock cachedBlock ) )
			{
				return cachedBlock ;
			}
			else
			{
				cachedBlock = new CachedBlock ( )
							  {
								  Sequence   = sequenceNumber ,
								  Metadata   = Blocks [ sequenceNumber ] ,
								  IsModified = false ,
								  Content    = DownloadRemoteBlock ( sequenceNumber ) . Result
							  } ;

				lock ( cachedBlock )
				{
					CachedBlocks . Add ( sequenceNumber , cachedBlock ) ;
				}

				return cachedBlock ;
			}
		}

	}

}
