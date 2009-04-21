﻿//-----------------------------------------------------------------------
// <copyright file="MetaDataHelpers.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

namespace Microsoft.Isam.Esent.Interop
{
    /// <summary>
    /// Helper methods for the ESENT API. These methods deal with database
    /// meta-data.
    /// </summary>
    public static partial class Api
    {
        /// <summary>
        /// Creates a dictionary which maps column names to their column IDs.
        /// </summary>
        /// <param name="sesid">The sesid to use.</param>
        /// <param name="tableid">The table to retrieve the information for.</param>
        /// <returns>A dictionary mapping column names to column IDs.</returns>
        public static Dictionary<string, JET_COLUMNID> GetColumnDictionary(JET_SESID sesid, JET_TABLEID tableid)
        {
            JET_COLUMNLIST columnlist;
            JetGetTableColumnInfo(sesid, tableid, string.Empty, out columnlist);
            try
            {
                // esent treats column names as case-insensitive, so we want the dictionary to be case insensitive as well
                var dict = new Dictionary<string, JET_COLUMNID>(
                    columnlist.cRecord, StringComparer.InvariantCultureIgnoreCase);
                if (columnlist.cRecord > 0)
                {
                    MoveBeforeFirst(sesid, columnlist.tableid);
                    while (TryMoveNext(sesid, columnlist.tableid))
                    {
                        string name = RetrieveColumnAsString(
                            sesid, columnlist.tableid, columnlist.columnidcolumnname, NativeMethods.Encoding);
                        var columnidValue =
                            (uint) RetrieveColumnAsUInt32(sesid, columnlist.tableid, columnlist.columnidcolumnid);

                        var columnid = new JET_COLUMNID() { Value = columnidValue };
                        dict.Add(name, columnid);
                    }
                }

                return dict;
            }
            finally
            {
                // Close the temporary table used to return the results
                JetCloseTable(sesid, columnlist.tableid);
            }
        }

        /// <summary>
        /// Iterates over all the columns in the table, returning information about each one.
        /// </summary>
        /// <param name="sesid">The session to use.</param>
        /// <param name="tableid">The table to retrieve column information for.</param>
        /// <returns>An iterator over ColumnInfo for each column in the table.</returns>
        public static IEnumerable<ColumnInfo> GetTableColumns(JET_SESID sesid, JET_TABLEID tableid)
        {
            return new EnumerableColumnInfo(sesid, tableid);
        }

        /// <summary>
        /// Iterates over all the columns in the table, returning information about each one.
        /// </summary>
        /// <param name="sesid">The session to use.</param>
        /// <param name="dbid">The database containing the table.</param>
        /// <param name="tablename">The name of the table.</param>
        /// <returns>An iterator over ColumnInfo for each column in the table.</returns>
        public static IEnumerable<ColumnInfo> GetTableColumns(JET_SESID sesid, JET_DBID dbid, string tablename)
        {
            JET_COLUMNLIST columnlist;
            JetGetColumnInfo(sesid, dbid, tablename, string.Empty, out columnlist);
            return EnumerateColumnInfos(sesid, columnlist);
        }

        /// <summary>
        /// Iterates over all the indexes in the table, returning information about each one.
        /// </summary>
        /// <param name="sesid">The session to use.</param>
        /// <param name="tableid">The table to retrieve index information for.</param>
        /// <returns>An iterator over an IndexInfo for each index in the table.</returns>
        public static IEnumerable<IndexInfo> GetTableIndexes(JET_SESID sesid, JET_TABLEID tableid)
        {
            JET_INDEXLIST indexlist;
            JetGetTableIndexInfo(sesid, tableid, string.Empty, out indexlist);
            return EnumerateIndexInfos(sesid, indexlist);
        }

        /// <summary>
        /// Iterates over all the indexs in the table, returning information about each one.
        /// </summary>
        /// <param name="sesid">The session to use.</param>
        /// <param name="dbid">The database containing the table.</param>
        /// <param name="tablename">The name of the table.</param>
        /// <returns>An iterator over an IndexInfo for each index in the table.</returns>
        public static IEnumerable<IndexInfo> GetTableIndexes(JET_SESID sesid, JET_DBID dbid, string tablename)
        {
            JET_INDEXLIST indexlist;
            JetGetIndexInfo(sesid, dbid, tablename, string.Empty, out indexlist);
            return EnumerateIndexInfos(sesid, indexlist);
        }

        /// <summary>
        /// Returns the names of the tables in the database.
        /// </summary>
        /// <param name="sesid">The session to use.</param>
        /// <param name="dbid">The database containing the table.</param>
        /// <returns>An iterator over the names of the tables in the database.</returns>
        public static IEnumerable<string> GetTableNames(JET_SESID sesid, JET_DBID dbid)
        {
            JET_OBJECTLIST objectlist;
            JetGetObjectInfo(sesid, dbid, out objectlist);
            try
            {
                MoveBeforeFirst(sesid, objectlist.tableid);
                while (TryMoveNext(sesid, objectlist.tableid))
                {
                    var flags = (uint) RetrieveColumnAsUInt32(sesid, objectlist.tableid, objectlist.columnidflags);
                    if (ObjectInfoFlags.System != ((ObjectInfoFlags) flags & ObjectInfoFlags.System))
                    {
                        yield return
                            RetrieveColumnAsString(
                                sesid, objectlist.tableid, objectlist.columnidobjectname, NativeMethods.Encoding);
                    }
                }
            }
            finally
            {
                // Close the temporary table used to return the results
                JetCloseTable(sesid, objectlist.tableid);
            }
        }

        /// <summary>
        /// Iterates over the information in the JET_INDEXLIST, returning information about each index.
        /// The table in the indexlist is closed when finished.
        /// </summary>
        /// <param name="sesid">The session to use.</param>
        /// <param name="indexlist">The indexlist to iterate over.</param>
        /// <returns>An iterator over IndexInfo for each index described in the JET_INDEXLIST.</returns>
        private static IEnumerable<IndexInfo> EnumerateIndexInfos(JET_SESID sesid, JET_INDEXLIST indexlist)
        {
            try
            {
                MoveBeforeFirst(sesid, indexlist.tableid);
                while (TryMoveNext(sesid, indexlist.tableid))
                {
                    yield return GetIndexInfoFromIndexlist(sesid, indexlist);
                }
            }
            finally
            {
                // Close the temporary table used to return the results
                JetCloseTable(sesid, indexlist.tableid);
            }
        }

        /// <summary>
        /// Create an IndexInfo object from the data in the current JET_INDEXLIST entry.
        /// </summary>
        /// <param name="sesid">The session to use.</param>
        /// <param name="indexlist">The indexlist to take the data from.</param>
        /// <returns>An IndexInfo object containing the information from that record.</returns>
        private static IndexInfo GetIndexInfoFromIndexlist(JET_SESID sesid, JET_INDEXLIST indexlist)
        {
            string name = RetrieveColumnAsString(
                sesid, indexlist.tableid, indexlist.columnidindexname, NativeMethods.Encoding);
            var lcid = (int) RetrieveColumnAsInt16(sesid, indexlist.tableid, indexlist.columnidLangid);
            var cultureInfo = new CultureInfo(lcid);
            var lcmapFlags = (uint) RetrieveColumnAsUInt32(sesid, indexlist.tableid, indexlist.columnidLCMapFlags);
            CompareOptions compareOptions = Conversions.CompareOptionsFromLCmapFlags(lcmapFlags);
            var grbit = (uint) RetrieveColumnAsUInt32(sesid, indexlist.tableid, indexlist.columnidgrbitIndex);

            IndexSegment[] segments = GetIndexSegmentsFromIndexlist(sesid, indexlist);

            return new IndexInfo(
                name,
                cultureInfo,
                compareOptions,
                segments,
                (CreateIndexGrbit) grbit);
        }

        /// <summary>
        /// Create an array of IndexSegment objects from the data in the current JET_INDEXLIST entry.
        /// </summary>
        /// <param name="sesid">The session to use.</param>
        /// <param name="indexlist">The indexlist to take the data from.</param>
        /// <returns>An array of IndexSegment objects containing the information for the current index.</returns>
        private static IndexSegment[] GetIndexSegmentsFromIndexlist(JET_SESID sesid, JET_INDEXLIST indexlist)
        {
            var numSegments = (int) RetrieveColumnAsInt32(sesid, indexlist.tableid, indexlist.columnidcColumn);
            Debug.Assert(numSegments > 0, "Index has zero index segments");

            var segments = new IndexSegment[numSegments];
            for (int i = 0; i < numSegments; ++i)
            {
                string columnName = RetrieveColumnAsString(
                    sesid, indexlist.tableid, indexlist.columnidcolumnname, NativeMethods.Encoding);
                var coltyp = (int) RetrieveColumnAsInt32(sesid, indexlist.tableid, indexlist.columnidcoltyp);
                var grbit =
                    (IndexKeyGrbit) RetrieveColumnAsInt32(sesid, indexlist.tableid, indexlist.columnidgrbitColumn);
                bool isAscending = IndexKeyGrbit.Ascending == grbit;
                var cp = (JET_CP) RetrieveColumnAsInt16(sesid, indexlist.tableid, indexlist.columnidCp);
                bool isASCII = JET_CP.ASCII == cp;

                segments[i] = new IndexSegment(columnName, (JET_coltyp) coltyp, isAscending, isASCII);

                if (i < numSegments - 1)
                {
                    Api.JetMove(sesid, indexlist.tableid, JET_Move.Next, MoveGrbit.None);
                }
            }

            return segments;
        }

        /// <summary>
        /// Iterates over the information in the JET_COLUMNLIST, returning information about each column.
        /// The table in the columnlist is closed when finished.
        /// </summary>
        /// <param name="sesid">The session to use.</param>
        /// <param name="columnlist">The columnlist to iterate over.</param>
        /// <returns>An iterator over ColumnInfo for each column described in the JET_COLUMNLIST.</returns>
        private static IEnumerable<ColumnInfo> EnumerateColumnInfos(JET_SESID sesid, JET_COLUMNLIST columnlist)
        {
            try
            {
                MoveBeforeFirst(sesid, columnlist.tableid);
                while (TryMoveNext(sesid, columnlist.tableid))
                {
                    yield return GetColumnInfoFromColumnlist(sesid, columnlist);
                }
            }
            finally
            {
                // Close the temporary table used to return the results
                JetCloseTable(sesid, columnlist.tableid);
            }
        }

        /// <summary>
        /// Create a ColumnInfo object from the data in the current JET_COLUMNLIST
        /// entry.
        /// </summary>
        /// <param name="sesid">The session to use.</param>
        /// <param name="columnlist">The columnlist to take the data from.</param>
        /// <returns>A ColumnInfo object containing the information from that record.</returns>
        private static ColumnInfo GetColumnInfoFromColumnlist(JET_SESID sesid, JET_COLUMNLIST columnlist)
        {
            string name = RetrieveColumnAsString(
                sesid, columnlist.tableid, columnlist.columnidcolumnname, NativeMethods.Encoding);
            var columnidValue = (uint) RetrieveColumnAsUInt32(sesid, columnlist.tableid, columnlist.columnidcolumnid);
            var coltypValue = (uint) RetrieveColumnAsUInt32(sesid, columnlist.tableid, columnlist.columnidcoltyp);
            uint codepageValue = (ushort) RetrieveColumnAsUInt16(sesid, columnlist.tableid, columnlist.columnidCp);
            var maxLength = (uint) RetrieveColumnAsUInt32(sesid, columnlist.tableid, columnlist.columnidcbMax);
            byte[] defaultValue = RetrieveColumn(sesid, columnlist.tableid, columnlist.columnidDefault);
            var grbitValue = (uint) RetrieveColumnAsUInt32(sesid, columnlist.tableid, columnlist.columnidgrbit);

            return new ColumnInfo(
                name,
                new JET_COLUMNID() { Value = columnidValue },
                (JET_coltyp) coltypValue,
                (JET_CP) codepageValue,
                (int) maxLength,
                defaultValue,
                (ColumndefGrbit) grbitValue);
        }
    }
}