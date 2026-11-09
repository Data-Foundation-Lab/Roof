using Roof.Common.Tools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Roof.Common.Networking.RoofData
{
    public class RoofDirectoryData
    {
        private string _hashedPath;
        /// <summary>
        /// Hashed path of the directory. (Primary Key Identifier)
        /// Dev Note: Can’t really use a raw name or path as a primary key (PK), as it could exist in multiple locations.
        /// </summary>
        public string HashedPath
        {
            get
            {
                if (_hashedPath == null)
                    _hashedPath = Security.XxHash64.GenerateHashedPath(Path);

                return _hashedPath;
            }
        }

        public void SetHashedPath(string newHashedPath) => _hashedPath = newHashedPath;

        /// <summary>
        /// Name of the directory.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Full path of the directory.
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// Size in bytes.
        /// </summary>
        public int Size { get; set; }
        
        /// <summary>
        /// Last modified timestamp in UTC.
        /// </summary>
        public DateTime LastModifiedUTC { get; set; }

        /// <summary>
        /// Files in the directory (Foreign Keys).
        /// </summary>
        //public Dictionary<string, RoofFileData>? Files { get; set; }
        public HashSet<string>? Files { get; set; }

        /// <summary>
        /// Subdirectories in the directory (Recursive Keys).
        /// </summary>
        public HashSet<string>? SubDirectories { get; set; }

        /// <summary>
        /// Parent Directory (Foreign Key).
        /// </summary>
        public string? ParentDirectory { get; set; }
    }
}
