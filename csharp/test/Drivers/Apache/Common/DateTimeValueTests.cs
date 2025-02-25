﻿/*
* Licensed to the Apache Software Foundation (ASF) under one or more
* contributor license agreements.  See the NOTICE file distributed with
* this work for additional information regarding copyright ownership.
* The ASF licenses this file to You under the Apache License, Version 2.0
* (the "License"); you may not use this file except in compliance with
* the License.  You may obtain a copy of the License at
*
*    http://www.apache.org/licenses/LICENSE-2.0
*
* Unless required by applicable law or agreed to in writing, software
* distributed under the License is distributed on an "AS IS" BASIS,
* WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
* See the License for the specific language governing permissions and
* limitations under the License.
*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Apache.Arrow.Adbc.Tests.Drivers.Apache.Hive2;
using Xunit;
using Xunit.Abstractions;

namespace Apache.Arrow.Adbc.Tests.Drivers.Apache.Common
{
    // TODO: When supported, use prepared statements instead of SQL string literals
    //      Which will better test how the driver handles values sent/received

    /// <summary>
    /// Validates that specific date, timestamp and interval values can be inserted, retrieved and targeted correctly
    /// </summary>
    public abstract class DateTimeValueTests<TConfig, TEnv> : TestBase<TConfig, TEnv>
        where TConfig : TestConfiguration
        where TEnv : HiveServer2TestEnvironment<TConfig>
    {
        // Spark handles microseconds but not nanoseconds. Truncated to 6 decimal places.
        const string DateTimeZoneFormat = "yyyy-MM-dd'T'HH:mm:ss'.'ffffffK";
        const string DateTimeFormat = "yyyy-MM-dd' 'HH:mm:ss";
        protected const string DateFormat = "yyyy-MM-dd";

        private static readonly DateTimeOffset[] s_timestampValues =
        [
#if NET5_0_OR_GREATER
            DateTimeOffset.UnixEpoch,
#endif
            DateTimeOffset.MinValue,
            DateTimeOffset.MaxValue,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(4))
        ];

        public DateTimeValueTests(ITestOutputHelper output, TestEnvironment<TConfig>.Factory<TEnv> testEnvFactory)
            : base(output, testEnvFactory) { }

        /// <summary>
        /// Validates if driver can send and receive specific Timstamp values correctly
        /// </summary>
        [SkippableTheory]
        [MemberData(nameof(TimestampData), "TIMESTAMP")]
        public async Task TestTimestampData(DateTimeOffset value, string columnType)
        {
            string columnName = "TIMESTAMPTYPE";
            using TemporaryTable table = await NewTemporaryTableAsync(Statement, string.Format("{0} {1}", columnName, columnType));

            string format = TestEnvironment.GetValueForProtocolVersion(DateTimeFormat, DateTimeZoneFormat)!;
            string formattedValue = $"{value.ToString(format, CultureInfo.InvariantCulture)}";
            DateTimeOffset truncatedValue = DateTimeOffset.ParseExact(formattedValue, format, CultureInfo.InvariantCulture);

            object expectedValue = TestEnvironment.GetValueForProtocolVersion(formattedValue, truncatedValue)!;
            await ValidateInsertSelectDeleteSingleValueAsync(
                table.TableName,
                columnName,
                expectedValue,
                "TO_TIMESTAMP(" + QuoteValue(formattedValue) + ")");
        }

        /// <summary>
        /// Validates if driver can send and receive specific no timezone Timstamp values correctly
        /// </summary>
        [SkippableTheory]
        [MemberData(nameof(TimestampData), "DATE")]
        public async Task TestDateData(DateTimeOffset value, string columnType)
        {
            string columnName = "DATETYPE";
            using TemporaryTable table = await NewTemporaryTableAsync(Statement, string.Format("{0} {1}", columnName, columnType));

            string formattedValue = $"{value.ToString(DateFormat, CultureInfo.InvariantCulture)}";
            DateTimeOffset truncatedValue = DateTimeOffset.ParseExact(formattedValue, DateFormat, CultureInfo.InvariantCulture);

            // Remove timezone offset
            object expectedValue = TestEnvironment.GetValueForProtocolVersion(formattedValue, new DateTimeOffset(truncatedValue.DateTime, TimeSpan.Zero))!;
            await ValidateInsertSelectDeleteSingleValueAsync(
                table.TableName,
                columnName,
                expectedValue,
                "TO_DATE(" + QuoteValue(formattedValue) + ")");
        }

        /// <summary>
        /// Tests INTERVAL data types (YEAR-MONTH and DAY-SECOND).
        /// </summary>
        /// <param name="intervalClause">The INTERVAL to test.</param>
        /// <param name="value">The expected return value.</param>
        /// <returns></returns>
        [SkippableTheory]
        [InlineData("INTERVAL 1 YEAR", "1-0")]
        [InlineData("INTERVAL 1 YEAR 2 MONTH", "1-2")]
        [InlineData("INTERVAL 2 MONTHS", "0-2")]
        [InlineData("INTERVAL -1 YEAR", "-1-0")]
        [InlineData("INTERVAL -1 YEAR 2 MONTH", "-0-10")]
        [InlineData("INTERVAL -2 YEAR 2 MONTH", "-1-10")]
        [InlineData("INTERVAL 1 YEAR -2 MONTH", "0-10")]
        [InlineData("INTERVAL 178956970 YEAR", "178956970-0")]
        [InlineData("INTERVAL 178956969 YEAR 11 MONTH", "178956969-11")]
        [InlineData("INTERVAL -178956970 YEAR", "-178956970-0")]
        [InlineData("INTERVAL 0 DAYS 0 HOURS 0 MINUTES 0 SECONDS", "0 00:00:00.000000000")]
        [InlineData("INTERVAL 1 DAYS", "1 00:00:00.000000000")]
        [InlineData("INTERVAL 2 HOURS", "0 02:00:00.000000000")]
        [InlineData("INTERVAL 3 MINUTES", "0 00:03:00.000000000")]
        [InlineData("INTERVAL 4 SECONDS", "0 00:00:04.000000000")]
        [InlineData("INTERVAL 1 DAYS 2 HOURS", "1 02:00:00.000000000")]
        [InlineData("INTERVAL 1 DAYS 2 HOURS 3 MINUTES", "1 02:03:00.000000000")]
        [InlineData("INTERVAL 1 DAYS 2 HOURS 3 MINUTES 4 SECONDS", "1 02:03:04.000000000")]
        [InlineData("INTERVAL 1 DAYS 2 HOURS 3 MINUTES 4.123123123 SECONDS", "1 02:03:04.123123000")] // Only to microseconds
        [InlineData("INTERVAL 106751990 DAYS 23 HOURS 59 MINUTES 59.999999 SECONDS", "106751990 23:59:59.999999000")]
        [InlineData("INTERVAL 106751991 DAYS 0 HOURS 0 MINUTES 0 SECONDS", "106751991 00:00:00.000000000")]
        [InlineData("INTERVAL -106751991 DAYS 0 HOURS 0 MINUTES 0 SECONDS", "-106751991 00:00:00.000000000")]
        [InlineData("INTERVAL -106751991 DAYS 23 HOURS 59 MINUTES 59.999999 SECONDS", "-106751990 00:00:00.000001000")]
        public async Task TestIntervalData(string intervalClause, string value)
        {
            string selectStatement = $"SELECT {intervalClause} AS INTERVAL_VALUE;";
            await SelectAndValidateValuesAsync(selectStatement, value, 1);
        }

        public static IEnumerable<object[]> TimestampData(string columnType)
        {
            foreach (DateTimeOffset timestamp in s_timestampValues)
            {
                yield return new object[] { timestamp, columnType };
            }
        }
    }
}
