using System ;
using System . Collections ;
using System . Collections . Generic ;
using System . Linq ;

using DreamRecorder . CloudFileSystem . FileSystem ;

using Microsoft . EntityFrameworkCore ;

namespace DreamRecorder . CloudFileSystem
{

	public class DataContext : DbContext
	{

		public DbSet <FileMetadata> FileMetadata { get ; set ; }

		public DbSet <BlockMetadata> BlockMetadata { get ; set ; }

		protected override void OnConfiguring ( DbContextOptionsBuilder optionsBuilder )
		{
			optionsBuilder . UseSqlServer (
										   Program .
											   Program . Current ? . Setting ? . SqlConnectionString
										?? "Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=CloudFileSystem;Integrated Security=True;" ) ; //todo
		}

	}

}
