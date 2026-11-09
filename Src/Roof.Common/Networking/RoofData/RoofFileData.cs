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
    public class RoofFileData
    {
        private string _hashedPath;
        /// <summary>
        /// Hashed path of the file. (Primary Key Identifier)
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
        /// Name of the file.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Full path of the file.
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// Size in bytes.
        /// </summary>
        public int Size { get; set; }

        /// <summary>
        /// Hash value of the content.
        /// </summary>
        public string FileHash { get; set; }

        /// <summary>
        /// Last modified timestamp in UTC.
        /// </summary>
        public DateTime LastModifiedUTC { get; set; }

        /// <summary>
        /// Parent Directory. (Foreign Key)
        /// </summary>
        public string ParentDirectory { get; set; }
        
        /// <summary>
        /// Actual file content/data in raw bytes. Should be in compressed and optimised format. To be only used when transmitting it.
        /// </summary>
        public byte[] Data { get; set; }

        /// <summary>
        /// Whether the data should be overwritten.
        /// </summary>
        public bool ShouldOverwriteData { get; set; }
        
        /// <summary>
        /// Whether data should be appended to the current data.
        /// </summary>
        public bool ShouldAppendData { get; set; }

        public void CompressData()
        {

        }

        public void DecompressData()
        {

        }
    }
}
