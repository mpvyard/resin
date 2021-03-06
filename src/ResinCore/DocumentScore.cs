﻿using System;
using System.Collections.Generic;
using System.Linq;
using Resin.Documents;

namespace Resin
{
    /// <summary>
    /// Scored posting. To combine inside a index, use doc ID. To combine between indices, use doc hash.
    /// </summary>
    public class DocumentScore
    {
        public int DocumentId { get; private set; }
        public double Score { get; set; }
        public SegmentInfo Ix { get; private set; }
        public UInt64 DocHash { get; set; }

        public DocumentScore(int documentId, UInt64 docHash, double score, SegmentInfo ix)
        {
            DocumentId = documentId;
            Score = score;
            Ix = ix;
            DocHash = docHash;
        }

        public DocumentScore(int documentId, double score, SegmentInfo ix)
        {
            DocumentId = documentId;
            Score = score;
            Ix = ix;
        }

        public void Add(DocumentScore score)
        {
            if (!score.DocumentId.Equals(DocumentId)) throw new ArgumentException("Document IDs differ. Cannot combine.", "score");

            Score = (Score + score.Score);
        }

        public static IList<DocumentScore> Not(IList<DocumentScore> source, IList<DocumentScore> exclude)
        {
            var dic = exclude.ToDictionary(x => x.DocumentId);
            var result = new List<DocumentScore>();

            foreach (var score in source)
            {
                DocumentScore exists;
                if (!dic.TryGetValue(score.DocumentId, out exists))
                {
                    result.Add(score);
                }
            }
            return result;
        }

        public static IList<DocumentScore> CombineOrPhrase(IList<DocumentScore> first, IList<DocumentScore> other)
        {
            if (first == null && other == null) return new DocumentScore[0];
            if (first == null) return other;
            if (other == null) return first;

            return first.Concat(other).GroupBy(x => x.DocumentId).Select(group =>
            {
                var list = group.ToArray();

                var top = list[0];
                for (int index = 1; index < list.Length; index++)
                {
                    top.Add(list[index]);
                }
                return top;
            }).ToArray();
        }

        public static IList<DocumentScore> CombineAndPhrase(IList<DocumentScore> first, IList<DocumentScore> other)
        {
            if (first == null && other == null) return new DocumentScore[0];
            if (first == null) return other;
            if (other == null) return first;

            var dic = other.ToDictionary(x => x.DocumentId);
            var result = new List<DocumentScore>(dic.Count);

            foreach (var score in first)
            {
                DocumentScore exists;
                if (dic.TryGetValue(score.DocumentId, out exists))
                {
                    score.Add(exists);
                    result.Add(score);
                }
            }
            return result;
        }

        public static IList<DocumentScore> CombineOr(IList<DocumentScore> first, IList<DocumentScore> other)
        {
            if (first == null && other == null) return new DocumentScore[0];
            if (first == null) return other;
            if (other == null) return first;

            return first.Concat(other).GroupBy(x => x.DocumentId).Select(group =>
            {
                var list = group.ToArray();
                
                var top = list[0];
                for (int index = 1; index < list.Length; index++)
                {
                    top.Add(list[index]);
                }
                return top;
            }).ToArray();
        }

        public static IList<DocumentScore> CombineAnd(IList<DocumentScore> first, IList<DocumentScore> other)
        {
            if (other == null) return first;

            var dic = other.GroupBy(s=>s.DocumentId).ToDictionary(x => x.Key, y=>y);

            var result = new List<DocumentScore>(dic.Count);

            foreach (var score in first.GroupBy(s => s.DocumentId))
            {
                IGrouping<int,DocumentScore> second;
                if (dic.TryGetValue(score.Key, out second))
                {
                    var list = score.ToArray();
                    var firstScore = list[0];

                    for(int i = 1;i< list.Length; i++)
                    {
                        firstScore.Add(list[i]);
                    }
                    foreach(var sc in second)
                    {
                        firstScore.Add(sc);
                    }
                    result.Add(firstScore);
                }
            }
            return result;
        }

        public override string ToString()
        {
            return string.Format("docid:{0} score:{1}", DocumentId, Score);
        }
    }

    public static class DocumentScoreExtensions
    {
        public static IList<DocumentScore> Sum(this IList<DocumentScore>[] scores)
        {
            if (scores.Length == 0) return new DocumentScore[0];

            if (scores.Length == 1) return scores[0].Sum();

            var first = scores[0];

            for (int i = 1; i < scores.Length; i++)
            {
                first = DocumentScore.CombineAnd(first, scores[i]);
            }
            return first;
        }

        public static IList<DocumentScore> Sum(this IList<DocumentScore> scores)
        {
            var sum = new List<DocumentScore>();
            DocumentScore tmp = null;
            foreach(var score in scores)
            {
                if (tmp == null)
                {
                    tmp = score;
                    continue;
                }
                if (score.DocumentId == tmp.DocumentId)
                {
                    tmp.Add(score);
                }
                else
                {
                    sum.Add(tmp);
                }
                tmp = score;
            }
            if (tmp != null)
            {
                sum.Add(tmp);
            }
            return sum;
        }

        public static IList<DocumentScore> SortByScoreAndTakeLatestVersion(
            this IList<IList<DocumentScore>> scores, int skip, int size, out int total)
        {
            if (scores.Count == 0)
            {
                total = 0;
                return new DocumentScore[0];
            }

            var first = scores[0];

            if (scores.Count == 1)
            {
                total = first.Count;
            }

            for (int i = 1; i < scores.Count; i++)
            {
                var upToDate = TakeLatestVersion(first, scores[i]);
                first = upToDate;
            }

            ((List<DocumentScore>)first).Sort(new DescendingDocumentScoreComparer());
            total = first.Count;

            var took = 0;
            var skipped = 0;
            var result = new List<DocumentScore>(size);

            for (int index = 0; index < first.Count; index++)
            {
                if (took == size)
                {
                    break;
                }

                if (skip > 0 && skipped++ < skip)
                {
                    continue;
                }

                result.Add(first[index]);
                took++;
            }
            
            return result;
        }
        
        public static IList<DocumentScore> TakeLatestVersion(
            IList<DocumentScore> first, IList<DocumentScore> second)
        {
            var unique = new Dictionary<UInt64, DocumentScore>();
            var result = new List<DocumentScore>();

            foreach (var score in first)
            {
                unique.Add(score.DocHash, score);
                result.Add(score);
            }

            foreach (var score in second)
            {
                DocumentScore exists;

                if (unique.TryGetValue(score.DocHash, out exists))
                {
                    exists = TakeLatestVersion(exists, score);
                }
                else
                {
                    unique.Add(score.DocHash, score);
                    result.Add(score);
                }
            }

            return result;
        }

        public static DocumentScore TakeLatestVersion(DocumentScore first, DocumentScore second)
        {
            if (!first.DocHash.Equals(second.DocHash)) throw new ArgumentException("Document hashes differ. Cannot take latest version.", "score");

            if (first.Ix.Version > second.Ix.Version)
            {
                return first;
            }
            return second;
        }
    }

    public class DescendingDocumentScoreComparer : IComparer<DocumentScore>
    {
        public int Compare(DocumentScore x, DocumentScore y)
        {
            if (x.Score < y.Score) return 1;
            if (x.Score > y.Score) return -1;
            return 0;
        }
    }

    public class DescendingScoreComparer : IComparer<double>
    {
        public int Compare(double x, double y)
        {
            if (x < y) return 1;
            if (x > y) return -1;
            return 0;
        }
    }
}