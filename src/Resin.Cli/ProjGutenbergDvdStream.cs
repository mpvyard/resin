﻿using System.Collections.Generic;
using System.IO;
using System.Text;
using System.IO.Compression;
using System;
using Resin.Documents;

namespace Resin
{
    public class ProjGutenbergDvdStream : DocumentStream
    {
        private readonly int _take;
        private readonly int _skip;
        private readonly string _directory;

        public ProjGutenbergDvdStream(string directory, int skip, int take)
            : base("uri")
        {
            _directory = directory;
            _skip = skip;
            _take = take;
        }

        public override IEnumerable<DocumentTableRow> ReadSource()
        {
            return ReadSourceAndAssignPk(ReadInternal());
        }

        private IEnumerable<DocumentTableRow> ReadInternal()
        {
            var files = Directory.GetFiles(_directory, "*.zip", SearchOption.AllDirectories);
            var skipped = 0;
            var took = 0;

            foreach (var zipFileName in files)
            {
                if (_skip > 0 && skipped++ < _skip)
                {
                    continue;
                }

                if (took == _take)
                {
                    break;
                }

                DocumentTableRow document = null;

                try
                {
                    using (var fileStream = new FileStream(zipFileName, FileMode.Open))
                    using (var zip = new ZipArchive(fileStream, ZipArchiveMode.Read))
                    {
                        ZipArchiveEntry txtFile = null;
                        foreach(var entry in zip.Entries)
                        {
                            if (entry.Name.EndsWith(".txt"))
                            {
                                txtFile = entry;
                                break;
                            }
                        }
                        if (txtFile != null)
                        {
                            using (var txtStream = txtFile.Open())
                            using (var reader = new StreamReader(txtStream))
                            {
                                var title = reader.ReadLine() + " " + reader.ReadLine();
                                var head = new StringBuilder();
                                var couldNotRead = false;
                                string encoding = null;

                                while (true)
                                {
                                    var line = reader.ReadLine();

                                    if (line == null)
                                    {
                                        couldNotRead = true;
                                        break;
                                    }
                                    else if (line.Contains("*** "))
                                    {
                                        break;
                                    }

                                    if (line.Contains("encoding: ASCII"))
                                    {
                                        encoding = line;
                                    }
                                    else
                                    {
                                        head.Append(" ");
                                        head.Append(line);
                                    }

                                }

                                if (encoding == null || couldNotRead)
                                {
                                    continue;
                                }

                                var body = reader.ReadToEnd();

                                document = new DocumentTableRow(
                                    new List<Field>
                                    {
                                new Field("title", title),
                                new Field("head", head),
                                new Field("body", body),
                                new Field("uri", zipFileName.Replace(_directory, ""))
                                    });

                            }
                        }
                        
                    }
                    
                }
                catch(Exception ex)
                {
                    Log.InfoFormat("unreadable file: {0} {1}", zipFileName, ex.Message);
                    continue;
                }

                if (document != null)
                {
                    yield return document;
                    took++;
                }
            }
        }
    }
}