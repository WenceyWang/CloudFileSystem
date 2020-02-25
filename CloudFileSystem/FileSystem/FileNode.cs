using System ;
using System . Collections ;
using System . Collections . Generic ;
using System . Linq ;
using System . Threading . Tasks ;

using DreamRecorder . ToolBox . General ;

using Microsoft . Extensions . DependencyInjection ;
using Microsoft . Extensions . Logging ;

namespace DreamRecorder . CloudFileSystem . FileSystem
{

	public class FileNode
	{

		public int ReferenceCount = 1 ;

		public DateTime ? ClosedTime { get ; set ; }

		public FileMetadata Metadata { get ; set ; }

		public List <BlockMetadata> Blocks { get ; set ; } = new List <BlockMetadata> ( ) ;

		public SortedList <long , CachedBlock> CachedBlocks { get ; set ; } =
			new SortedList <long , CachedBlock> ( ) ;


		public long BlockSize => CloudFileSystem . BlockSize ;

		public FileNode ( FileMetadata fileMetadata ) => Metadata = fileMetadata ;

		public void Flush ( )
		{
			List <Task> tasks = new List <Task> ( ) ;

			lock ( CachedBlocks )
			{
				foreach ( KeyValuePair <long , CachedBlock> cachedPair in CachedBlocks )
				{
					CachedBlock cachedBlock = cachedPair . Value ;
					tasks . Add ( cachedBlock . UpdateRemoteBlock ( ) ) ;
				}
			}

			Task . WaitAll ( tasks . ToArray ( ) ) ;

			CloudFileSystem . Current . FlushMetadata ( ) ;
		}

		public CachedBlock GetBlock ( int sequenceNumber )
		{
			CachedBlock cachedBlock ;

			lock ( CachedBlocks )
			{
				if ( CachedBlocks . TryGetValue ( sequenceNumber , out cachedBlock ) )
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
								  } ;
					CachedBlocks . Add ( sequenceNumber , cachedBlock ) ;
				}
			}

			lock ( cachedBlock )
			{
				if ( cachedBlock . Metadata . RemoteFileName != null )
				{
					cachedBlock . DownloadRemoteBlock ( ) . Wait ( ) ;
				}
				else
				{
					cachedBlock . Content = new byte[ BlockSize ] ;
				}

				return cachedBlock ;
			}
		}

		#region Logger

		private static ILogger Logger
			=> _logger ??= StaticServiceProvider . Provider . GetService <ILoggerFactory> ( ) .
												   CreateLogger <FileNode> ( ) ;


		private static ILogger _logger ;

		#endregion

	}

}
