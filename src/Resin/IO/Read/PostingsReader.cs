using System;
using System.Collections.Generic;
using System.IO;

namespace Resin.IO.Read
{
    public class PostingsReader : IDisposable
    {
        private readonly StreamReader _sr;

        public PostingsReader(StreamReader sr)
        {
            _sr = sr;
        }
        
        public IEnumerable<DocumentPosting> Read(Term term)
        {
            string line;
            var data = string.Empty;

            while ((line = _sr.ReadLine()) != null)
            {
                var parts = line.Split(':');
                var token = parts[1];

                if (token == null)
                {
                    throw new DataMisalignedException("TSNHappen");
                }

                var test = new Term(parts[0], new Word(token));

                if (test.Equals(term))
                {
                    data = parts[2];
                    break;
                }
            }

            if (!string.IsNullOrWhiteSpace(data))
            {
                var bytes = Convert.FromBase64String(data);

                using (var memStream = new MemoryStream(bytes))
                {
                    foreach (var posting in Deserialize(memStream))
                    {
                        posting.Field = term.Field;
                        yield return posting;
                    }
                }
            }
        }

        private IEnumerable<DocumentPosting> Deserialize(Stream stream)
        {
            return (IList<DocumentPosting>)BinaryFile.Serializer.Deserialize(stream);
        }

        public void Dispose()
        {
            if (_sr != null)
            {
                _sr.Dispose();
            }
        }
    }
}