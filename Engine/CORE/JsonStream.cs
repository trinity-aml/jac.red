using Newtonsoft.Json;
using System.IO;
using System.IO.Compression;

namespace JacRed.Engine.CORE
{
    public static class JsonStream
    {
        #region Read
        public static T Read<T>(string path)
        {
            var serializer = new JsonSerializer();

            using (Stream file = File.Exists($"{path}.gz") ? new GZipStream(File.OpenRead($"{path}.gz"), CompressionMode.Decompress) : File.OpenRead(path))
            {
                using (var sr = new StreamReader(file))
                {
                    using (var jsonTextReader = new JsonTextReader(sr))
                    {
                        return serializer.Deserialize<T>(jsonTextReader);
                    }
                }
            }
        }
        #endregion

        #region Write
        public static void Write(string path, object db)
        {
            var settings = new JsonSerializerSettings()
            {
                Formatting = Formatting.Indented
            };

            var serializer = JsonSerializer.Create(settings);

            using (var sw = new StreamWriter(new GZipStream(File.OpenWrite($"{path}.gz"), CompressionMode.Compress)))
            {
                using (var jsonTextWriter = new JsonTextWriter(sw))
                {
                    serializer.Serialize(jsonTextWriter, db);
                }
            }
        }
        #endregion
    }
}
