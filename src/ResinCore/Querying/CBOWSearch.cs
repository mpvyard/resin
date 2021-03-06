﻿using StreamIndex;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Resin.Querying
{
    public class CBOWSearch : Search
    {
        public CBOWSearch(IFullTextReadSession session, IScoringSchemeFactory scoringFactory)
            : base(session, scoringFactory)
        {
        }

        public void Search(QueryContext ctx)
        {
            var phraseQuery = (PhraseQuery)ctx.Query;
            var tokens = phraseQuery.Values;
            var addressesMatrix = new List<IList<BlockInfo>>();

            for (int index = 0; index < tokens.Count; index++)
            {
                var time = Stopwatch.StartNew();
                var token = tokens[index];
                var addresses = new List<BlockInfo>();

                using (var reader = GetTreeReader(ctx.Query.Key))
                {
                    if (ctx.Query.Fuzzy)
                    {
                        var words = reader.SemanticallyNear(token, ctx.Query.Edits(token));

                        foreach (var word in words)
                        {
                            addresses.Add(word.PostingsAddress.Value);
                        }
                        
                    }
                    else if (ctx.Query.Prefix)
                    {
                        var words = reader.StartsWith(token);
                        foreach (var word in words)
                        {
                            addresses.Add(word.PostingsAddress.Value);
                        }
                    }
                    else
                    {
                        var word = reader.IsWord(token);
                        if (word != null)
                        {
                            addresses.Add(word.PostingsAddress.Value);
                        }
                    }
                    addressesMatrix.Add(addresses);
                }

                Log.InfoFormat("{0} hit/-s for term {1}:{2} in {3}",
                    addresses.Count, ctx.Query.Key, token, time.Elapsed);
            }

            var postings = Session.ReadPositions(addressesMatrix);

            if (postings.Count < tokens.Count)
            {
                ctx.Scores = new List<DocumentScore>();
            }
            else
            {
                ctx.Scores = Score(postings);
            }
        }

        private IList<DocumentScore> Score(IList<IList<DocumentPosting>> postings)
        {
            if (postings.Count == 1)
            {
                return Score(postings[0]);
            }

            var weights = new DocumentScore[postings[0].Count][];

            SetWeights(postings, weights);

            var timer = Stopwatch.StartNew();

            var scoreDic = new Dictionary<int, DocumentScore>();

            foreach(DocumentScore[] score in weights)
            {
                if (score != null)
                {
                    DocumentScore sum = score[0];

                    if (sum == null)
                    {
                        continue;
                    }

                    for (int i = 1; i < score.Length; i++)
                    {
                        var s = score[i];
                        if (s == null)
                        {
                            sum = null;
                            break;
                        }
                        sum.Add(s);
                    }
                    if (sum != null)
                    {
                        DocumentScore existing;
                        if (scoreDic.TryGetValue(sum.DocumentId, out existing))
                        {
                            if (sum.Score > existing.Score)
                            {
                                scoreDic[sum.DocumentId] = sum;
                            }
                        }
                        else
                        {
                            scoreDic[sum.DocumentId] = sum;
                        }
                    }
                }
            }

            Log.DebugFormat("scored weights in {0}", timer.Elapsed);

            var notObsolete = new List<DocumentScore>();

            foreach (var score in scoreDic.Values)
            {
                var docHash = Session.ReadDocHash(score.DocumentId);

                if (!docHash.IsObsolete)
                {
                    score.DocHash = docHash.Hash;
                    notObsolete.Add(score);
                }
            }
            return notObsolete;
        }

        private void SetWeights(IList<IList<DocumentPosting>> postings, DocumentScore[][] weights)
        {
            int maxDistance = postings.Count - 1;
            var timer = Stopwatch.StartNew();
            var first = postings[0];

            for (int index = 1; index < postings.Count; index++)
            {
                var pass = index - 1;
                var second = postings[index];
                var count = Score(
                    weights, ref first, second, maxDistance, postings.Count - 1, pass);

                Log.DebugFormat(
                    "found {0} postings at word vector position {1}",
                    count, pass);
            }

            Log.DebugFormat("created weight matrix with {0} rows in {1}",
                    weights.Length, timer.Elapsed);
        }

        private int Score (
            DocumentScore[][] weights, ref IList<DocumentPosting> list1, 
            IList<DocumentPosting> list2, int maxDistance, int numOfPasses, int passIndex)
        {
            var count = 0;
            var cursor1 = 0;
            var cursor2 = 0;

            while (cursor1 < list1.Count && cursor2 < list2.Count)
            {
                var p1 = list1[cursor1];
                var p2 = list2[cursor2];

                if (p2.DocumentId > p1.DocumentId)
                {
                    cursor1++;
                    continue;
                }
                else if (p1.DocumentId > p2.DocumentId)
                {
                    cursor2++;
                    continue;
                }

                int distance = p1.Data - p2.Data;
                int absDistance = Math.Abs(distance);

                //    Log.DebugFormat("pass {0}: d of {1}:{2} and {3}:{4} = {5}",
                //            passIndex, p1.DocumentId, p1.Data, p2.DocumentId, p2.Data, distance);

                if (absDistance <= maxDistance)
                {
                    var score = (double)1 / absDistance;

                    if (distance < 0)
                    {
                        score -= Math.Log(absDistance);
                    }

                    var documentScore = new DocumentScore(p1.DocumentId, score, Session.Version);

                    if (weights[cursor1] == null)
                    {
                        weights[cursor1] = new DocumentScore[numOfPasses];
                        weights[cursor1][passIndex] = documentScore;
                    }
                    else
                    {
                        if (weights[cursor1][passIndex] == null || 
                            weights[cursor1][passIndex].Score < score)
                        {
                            weights[cursor1][passIndex] = documentScore;
                        }
                    }

                    //Log.DebugFormat("{0}:{1}:{2} scored {3}, distance {4}",
                    //    passIndex, p1.DocumentId, p1.Data, score, distance);

                    count++;

                    if (absDistance == 1)
                    {
                        cursor1++;
                        continue;
                    }
                }
                else if (distance < 0)
                {
                    cursor1++;
                    continue;
                }

                cursor2++;
            }

            return count;
        }
    }
}
