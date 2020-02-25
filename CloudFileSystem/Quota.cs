using System ;
using System . Collections ;
using System . Collections . Generic ;
using System . Linq ;

namespace DreamRecorder . CloudFileSystem
{

	public struct Quota
	{

		public long TotalQuota { get ; set ; }

		public long RemainingQuota { get ; set ; }

	}

}
