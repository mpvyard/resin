﻿using System;
using System.Diagnostics;

namespace Resin
{
    [DebuggerDisplay("{Value}")]
    public struct Field
    {
        private readonly string _value;

        public string Value { get { return _value; } }
        
        public string Key { get; private set; }
        public bool Store { get; private set; }
        public bool Analyze { get; private set; }
        public bool Index { get; private set; }

        public Field(string key, object value, bool store = true, bool analyze = true, bool index = true)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("key");
            if (value == null) throw new ArgumentNullException("value");

            Key = key;
            Store = store;
            Analyze = analyze;
            Index = index;

            if (value is DateTime)
            {
                _value = ((DateTime)value).Ticks.ToString();
            }
            else if (value is string)
            {
                _value = value.ToString();
            }
            else 
            {
                // Assumes all values that are not DateTime or string must be Int32.

                // TODO: implement native number indexes

                var len = int.MaxValue.ToString().Length;
                _value = value.ToString().PadLeft(len, '0');
            }
        }
    }
}