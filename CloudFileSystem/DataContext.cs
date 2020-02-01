using System ;
using System.Collections ;
using System.Collections.Generic ;
using System.Linq ;

using Microsoft.EntityFrameworkCore ;

namespace DreamRecorder . CloudFileSystem
{

	public class DataContext : DbContext
	{

		public DbSet <FileMetadata> FileMetadata { get ; set ; }

		public DbSet <BlockMetadata> BlockMetadata { get ; set ; }

		protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
		{
			optionsBuilder . UseSqlServer (Program.Current?.Setting?.SqlConnectionString??"") ;//todo
		}

	}

}