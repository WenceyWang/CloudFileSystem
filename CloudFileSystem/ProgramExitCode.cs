using System ;
using System . Collections ;
using System . Collections . Generic ;
using System . Linq ;

using DreamRecorder . ToolBox . CommandLine ;

namespace DreamRecorder . CloudFileSystem
{

	public class ProgramExitCode : ProgramExitCode <ProgramExitCode>
	{

		public static readonly ProgramExitCode InvalidSetting = ( ProgramExitCode ) 3 ;

	}

}
