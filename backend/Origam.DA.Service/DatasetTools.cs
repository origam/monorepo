#region license
/*
Copyright 2005 - 2021 Advantage Solutions, s. r. o.

This file is part of ORIGAM (http://www.origam.org).

ORIGAM is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

ORIGAM is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with ORIGAM. If not, see <http://www.gnu.org/licenses/>.
*/
#endregion

using System;
using System.Xml;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using Origam.Workbench.Services;
using Origam.Schema.GuiModel;
using Origam.Schema.EntityModel;
using Origam.Service.Core;

namespace Origam.DA;
/// <summary>
/// Summary description for DatasetTools.
/// </summary>
public class DatasetTools
{
	public DatasetTools()
	{
	}
	public static void Clear(DataSet data)
	{
		IDictionary<DataColumn, string> expressions = RemoveExpressions(data, true);
		try {
			// AcceptChanges is needed to prevent NPE when refreshing 
			// XmlDataDocument and reverting newly created rows
			data.AcceptChanges();
			data.EnforceConstraints = false;
			foreach (DataTable dt in data.Tables)
			{
				// cannot use BeginLoadData/EndLoadData beause in some more
				// difficult master-detail scenarios XmlDataDocument is crashing
				// when syncing deleted rows
				//dt.BeginLoadData();
				int count = dt.Rows.Count;
				for (int i = 0; i < count; i++)
				{
					data.Tables[dt.TableName].Rows.RemoveAt(0);
				}
				dt.AcceptChanges();
				//dt.EndLoadData();
			}
			data.EnforceConstraints = true;
		} 
		finally
		{
			SetExpressions(expressions);
		}
	}
	public static bool IsRowAggregated(DataRow row)
	{
		foreach(DataRelation relation in row.Table.ParentRelations)
		{
			foreach(DataColumn column in relation.ParentTable.Columns)
			{
				if(column.Expression.IndexOf("Child(") > 0)
				{
					return true;
				}
			}
			foreach(DataRow parentRow in row.GetParentRows(relation))
			{
				return IsRowAggregated(parentRow);
			}
		}
		return false;
	}
	public static string DateExpression(object dateValue)
	{
		DateTime date;
		
		if(dateValue is DateTime)
		{
			date = (DateTime)dateValue;
		}
		else if(dateValue == null)
		{
			return "null";
		}
		else
		{
			throw new ArgumentOutOfRangeException("dateValue", dateValue, ResourceUtils.GetString("OnlyDatetimeSupported"));
		}
		string result = "#" + date.Month + "/" + date.Day  + "/" + date.Year;
		
		if(date.Ticks != 0)
		{
			result += " " + date.TimeOfDay.Hours + ":" + date.TimeOfDay.Minutes + ":" + date.TimeOfDay.Seconds + "." + date.TimeOfDay.Milliseconds;
		}
		result += "#";
		return result;
	}
	public static string NumberExpression(object numberValue)
	{
		return Convert.ToString(numberValue, System.Globalization.CultureInfo.InvariantCulture);
	}
	public static string TextExpression(string text)
	{
		return "'" + text.Replace("'", "''") + "'";
	}
	public static bool HasRowValidParent(DataRow row)
	{
		foreach(DataRelation relation in row.Table.ParentRelations)
		{
			if(relation.ParentTable != relation.ChildTable) //skip self joins, no parent in self join is a root row, which is fine
			{
				DataRow[] parentRows = row.GetParentRows(relation);
				if(parentRows == null || parentRows.Length == 0) return false;
			}
		}
		return true;
	}
	public static void BeginLoadData(DataSet dataset)
	{
		foreach(DataTable targetTable in dataset.Tables)
		{
			targetTable.BeginLoadData();
            OrigamDataTable ot = targetTable as OrigamDataTable;
            if (ot != null)
            {
                ot.IsLoading = true;
            }
		}
	}
	public static void EndLoadData(DataSet dataset)
	{
		foreach(DataTable targetTable in dataset.Tables)
		{
            try
            {
                OrigamDataTable ot = targetTable as OrigamDataTable;
                if (ot != null)
                {
                    ot.IsLoading = false;
                }
                targetTable.EndLoadData();
            }
            catch (ConstraintException)
            {
                throw new ConstraintException(String.Format("Failed to enable constraint on `{0}' dataset: `{1}'"
                    , dataset.DataSetName
                    , DatasetTools.GetDatasetErrors(dataset)
                    ));
            }
		}
	}
	public static object[] PrimaryKey(DataRow row)
	{
		// store _currentKey
		DataColumn[] primaryKey = row.Table.PrimaryKey;
		string PkNames = "";
		bool first = true;
		object[] key = new object[primaryKey.Length];
		int i = 0;
		foreach(DataColumn col in primaryKey)
		{
			if(!first)
			{
				PkNames += ", ";
			}
			else
			{
				first = false;
			}
			PkNames += col.ColumnName;
			
			DataRowVersion version = (row.RowState == DataRowState.Deleted ? DataRowVersion.Original : DataRowVersion.Default);
			key[i++] = row[col, version];
		}
		return key;
	}
	public static string GetDatasetErrors(DataSet dataset)
	{
		StringBuilder result = new StringBuilder();
		foreach(DataTable table in dataset.Tables)
		{
			foreach(DataRow row in table.GetErrors())
			{
				string tableName = table.DisplayExpression;
				if(tableName == null) tableName = table.TableName;
				result.AppendFormat("{0}: Id: {3} {1}{2}", 
                    tableName, row.RowError, Environment.NewLine,
                    row.Table.PrimaryKey.Length > 0 ? 
                    row[row.Table.PrimaryKey[0]] : "N/A");
			}
		}
		return result.ToString();
	}
	public static DataRow GetErrorRow(DataTable table)
	{
		if(table != null)
		{
			foreach(DataRow row in table.GetErrors())
			{
				return row;
			}
		}
		return null;
	}
	public static string GetRowDescription(DataRow row)
	{
		DataTable table = row.Table;
		if(table.ExtendedProperties.Contains(Const.DescribingField))
		{
			string field = (string)table.ExtendedProperties[Const.DescribingField];
			if(table.Columns.Contains(field))
			{
				object result = row[field, 
						(row.RowState == DataRowState.Deleted 
							? DataRowVersion.Original 
							: DataRowVersion.Default)
					];
				if(row.RowState == DataRowState.Modified)
				{
					object originalResult = row[field, DataRowVersion.Original];
					if(result == DBNull.Value && originalResult != DBNull.Value)
					{
						result = originalResult;
					}
					else if(result != DBNull.Value && originalResult != DBNull.Value
						&& ! result.Equals(originalResult))
					{
						result = ConvertValueToString(originalResult) 
							+ " -> " + ConvertValueToString(result);
					}
				}
				if(result != DBNull.Value)
				{
					return ConvertValueToString(result);
				}
			}
		}
		return null;
	}
	private static string ConvertValueToString(object value)
	{
		if(value is string)
		{
			return (string)value;
		}
		else if(value is decimal)
		{
			return ((decimal)value).ToString("0.#");
		}
		else
		{
			return value.ToString();
		}
	}
	/// <summary>
	/// Goes through all source tables and merges them to the target dataset's tables with same names.
	/// </summary>
	/// <param name="inout_dsTarget"></param>
	/// <param name="in_dsSource"></param>
	/// <param name="in_bTrueDelete"></param>
	/// <param name="in_bPreserveChanges"></param>
	/// <param name="in_bSourceIsFragment"></param>
    public static bool MergeDataSet(
        DataSet inout_dsTarget, 
        DataSet in_dsSource, 
        IDictionary<string, IList<KeyValuePair<object, DataMergeChange>>> changeList,
        MergeParams mergeParams) 
	{
		bool changed = false;
		IDictionary<DataColumn, string> expressions = RemoveExpressions(inout_dsTarget, true);
		try
		{
			foreach (DataTable table in in_dsSource.Tables)
			{
				if (inout_dsTarget.Tables.Contains(table.TableName))
				{
					IList<KeyValuePair<object, DataMergeChange>> tableChangeList = null;
					if (changeList != null) tableChangeList = new List<KeyValuePair<object, DataMergeChange>>();
					if (MergeDataTable(inout_dsTarget.Tables[table.TableName], 
                        table, tableChangeList, expressions, mergeParams))
					{
						changed = true;
						if (changeList != null)
						{
							changeList.Add(table.TableName, tableChangeList);
						}
					}
				}
			}
		}
		finally
		{
			SetExpressions(expressions);
		}
		return changed;
	}
	/// <summary>
	/// Substitute for DataSet.Merge().
	/// Copies all differences from in_dtSource into inout_dtTarget,
	/// without accepting the changes (RowStates after can be either
	/// Unchanged, Modified, Added, or Deleted.)
	/// - When row exist with same PK, row fields in the target are forced
	/// with the value of same row fields from the source.
	/// - When row does not exist in target with same PK, it is inserted in target.
	/// - When row exists only in target, it is deleted from target (not really : isDeleted is set to true).
	/// Prerequisities :
	/// - Rows in source & target must come from same table
	/// - Columns must be the same in source & target, including same Primary Key
	/// - Columns must be in same order (same ordinal position) in source & target
	/// - Row order follows the order of primary keys (=> PK is a "clustered index")
	/// Notes :
	/// - DELETEs are actually turned into UPDATE ... SET isDeleted = 1,
	/// unless in_bTrueDelete is set to 'true'.
	/// - RowState is changed by this action only where differences exist (Modified, Added, Deleted)
	/// </summary>
	/// <param name="inout_drTarget">The target table (altered by this method)</param>
	/// <param name="in_dtSource">The source table (unaltered)</param>
	/// <param name="in_bTrueDelete">When row exists only in target, it is either actually deleted (true), or set with isDeleted = 1 (false)</param>
	/// <param name="in_bPreserveChanges">When row field is different in target, set it from source table (false), or keep current value if it was modified (true)</param>
	/// <param name="in_bSourceIsFragment">When set to true, this indicates that the source is a fragment, and doe not contain all rows of the target. In this case, the detection of rows that exist ony in target is skipped (delete case is skipped).</param>
    public static bool MergeDataTable(
        DataTable inout_dtTarget, 
        DataTable in_dtSource, 
        IList<KeyValuePair<object, DataMergeChange>> changeList, 
        IDictionary<DataColumn, string> expressions,
        MergeParams mergeParams)
	{
		int iCol;
		DataRow drSource, drTarget;
		int nbRowSource = in_dtSource.Rows.Count;// optim
		int nbCol = in_dtSource.Columns.Count;// optim
		bool bDoIt;
		bool bRowModified;
		bool changed = false;
		// Prerequisities : column names in each DataTable must match
		DataColumnCollection colsSource, colsTarget;
		colsSource = in_dtSource.Columns;
		colsTarget = inout_dtTarget.Columns;
		Hashtable changedColumns = new Hashtable();
		// Find position of PK columns (from source table, assumed same PK on target table)
		bool hasPrimaryKeys = true;
		//int iPk0 = 0, iPk1;// optim
		object[] aRowKey = null;
		DataColumn[] adcPK = null;
		// If table has no primary key, we will just add all the rows
		if(in_dtSource.PrimaryKey.Length == 0)
		{
			hasPrimaryKeys = false;
		}
		else
		{
			adcPK = inout_dtTarget.PrimaryKey;
			aRowKey = new object[adcPK.Length];
		}
		// for each row in source, fetch matching target row with same PK and update
		for (int iRowSource = 0 ; nbRowSource > iRowSource ; ++iRowSource) 
		{
			drSource = in_dtSource.Rows[iRowSource];
			drTarget = null;
			if(hasPrimaryKeys)
			{
				for(int i=0; i<adcPK.Length; i++)
				{
					// find source key value by target's key column name
					if(drSource.RowState == DataRowState.Deleted)
					{
						aRowKey[i] = ConvertValue(drSource[adcPK[i].ColumnName, DataRowVersion.Original], adcPK[i].DataType);
					}
					else
					{
						aRowKey[i] = ConvertValue(drSource[adcPK[i].ColumnName], adcPK[i].DataType);
					}
				}
				// first we make sure that deleted source is not already deleted
				// in target, if it is deleted there is nothing to do
				if (drSource.RowState == DataRowState.Deleted)
				{
					DataRow deletedRow = inout_dtTarget.Rows.Cast<DataRow>()
						.FirstOrDefault(
							row => row.RowState == DataRowState.Deleted &&
							       PrimaryKey(row).SequenceEqual(aRowKey)
						);
					if (deletedRow != null)
					{
						continue; 
					}
				}
				// Locate source row in target 
				drTarget = inout_dtTarget.Rows.Find(aRowKey); 
			}
			if (null == drTarget)
			{
				try
				{
					drTarget = inout_dtTarget.NewRow();
					if (mergeParams.PreserveNewRowState && drSource.RowState == DataRowState.Deleted)
                    {
                        // We insert the row and delete it in the target so it
                        // can be deleted from the database later if needed
                        CopyOriginalRecordVersion(inout_dtTarget, drSource, drTarget);
                        drTarget.Delete();
                    }
                    else if (mergeParams.PreserveNewRowState && drSource.RowState == DataRowState.Modified)
					{
						// We need to keep the original values, if the source row has been modified
						CopyOriginalRecordVersion(inout_dtTarget, drSource, drTarget);
						// And we overwrite them with the current values, thus making the row exactly
						// same state as the source row (original values, current values and state = modified)
						drTarget.BeginEdit();
						CopyRecordValues(drSource, DataRowVersion.Current, 
							drTarget, false);
					}
					else
					{
						// For any other states, we import the values
						drTarget.BeginEdit();
						CopyRecordValues(drSource, DataRowVersion.Current, 
							drTarget, false);
						// And we add the row to the table
						try
						{
							inout_dtTarget.Rows.Add(drTarget);
						}
						catch (System.Data.NoNullAllowedException e)
						{
							throw new System.Data.NoNullAllowedException(
								String.Format("Adding a new row failed for table `{0}' ({1})",
								inout_dtTarget, e.Message));
						}
					}
					if (mergeParams.PreserveNewRowState && drSource.RowState == DataRowState.Unchanged)
					{
						// Set rowstate to Unchanged in target, if the source row was Unchanged,
						// because if we load the row from a database, we want to keep the status.
						drTarget.AcceptChanges();
						System.Diagnostics.Debug.Assert(drTarget.RowState == DataRowState.Unchanged);
					}
					else if(drSource.RowState != DataRowState.Deleted)
					{
						// for any other than Unchanged, we fill the Created and CreatedBy
						UpdateOrigamSystemColumns(drTarget, true, mergeParams.ProfileId);
					}
					drTarget.EndEdit();
					changed = drTarget.RowState != DataRowState.Unchanged;
					if (changeList != null)
					{
						changeList.Add(new KeyValuePair<object, DataMergeChange>(
							aRowKey[0], new DataMergeChange(null, DataRowState.Added)));
					}
				}
				catch (Exception)
				{
					throw;
				}
			} // eof insert case
			else
			{ // row exists in target
			  // and is deleted in the source
				if (drSource.RowState == DataRowState.Deleted)
				{
					drTarget.Delete();
					changed = true;
					if (changeList != null)
					{
						changeList.Add(new KeyValuePair<object, DataMergeChange>(
							aRowKey[0], new DataMergeChange(null, DataRowState.Deleted)));
					}
				}
				else
				{
					drTarget.BeginEdit();
					// Compare & update fields (UPDATE ?)
					bRowModified = false;
					bool shouldUpdateSystemColumns = false;
					for (iCol = 0; inout_dtTarget.Columns.Count > iCol; ++iCol)
					{
						string colName = inout_dtTarget.Columns[iCol].ColumnName;
						if (in_dtSource.Columns.Contains(colName))
						{
							// Column with same name exists in the source, so we can copy it to the target
							// we only update, if values change
							if (!drTarget[colName].Equals(drSource[colName]))
							{
								// "Default version" value in target is different from source.
								bDoIt = true;
								// special case : should we preserve changes ?
								if (mergeParams.PreserveChanges)
								{
									// if changes in target must be preserved, then
									// check whether this row was modified, using versions
									// proof : at this step, the record can either be Unchanged or Modified, but neither Added, Deleted nor Detached.
									//System.Diagnostics.Debug.Assert(drTarget.HasVersion(DataRowVersion.Original));
									if (drTarget.HasVersion(DataRowVersion.Original)
										&& !drTarget[colName].Equals(drTarget[colName, DataRowVersion.Original]))
									{
										// this field was changed (not the same value) ==> preserve the change
										bDoIt = false;
										//TODO : raise an event for conflict management.
									}
								}
								if (bDoIt)
								{
									DataColumn col = inout_dtTarget.Columns[colName];
									if ((expressions == null) || !expressions.ContainsKey(col))
									{
										drTarget[colName] = DatasetTools.ConvertValue(drSource[colName], col.DataType);
										bRowModified = true;
										if (!changedColumns.Contains(col.ExtendedProperties["Id"]))
										{
											changedColumns.Add(col.ExtendedProperties["Id"], col);
											if (col.ExtendedProperties.Contains(Const.IsDatabaseField) &&
											    (bool) col.ExtendedProperties[Const.IsDatabaseField])
											{
												shouldUpdateSystemColumns = true;
											}
										}
									}
								}
							}
						} // eof exists source column name
					}
					if (bRowModified)
					{
						// only put RecordUpdated and RecordUpdatedBy if the data has actually changed
						if (shouldUpdateSystemColumns)
						{
							UpdateOrigamSystemColumns(drTarget, false, mergeParams.ProfileId, changedColumns);
						}
						drTarget.EndEdit();
						changed = true;
						if (changeList != null)
						{
							changeList.Add(new KeyValuePair<object, DataMergeChange>
								(aRowKey[0], new DataMergeChange(changedColumns, DataRowState.Modified)));
						}
					}
					else
					{
						drTarget.CancelEdit();
					}
					// sync the row state (e.g. after saving data to db the state changes from Added to Unchanged)
					if (drSource.RowState == DataRowState.Unchanged && drTarget.RowState == DataRowState.Added)
					{
						drTarget.AcceptChanges();
					}
				} // eof not deleted
			} // eof update case
		} // eof for iRowSource
		if (! mergeParams.SourceIsFragment) 
		{
			// for each row in source, fetch matching target row with same PK,
			// and delete row in target if not found
			if (mergeParams.TrueDelete & hasPrimaryKeys) 
			{
				adcPK = in_dtSource.PrimaryKey;
				aRowKey = new object[adcPK.Length];
				var rowsToDelete = new List<DataRow>();					
				int nbRowTarget = inout_dtTarget.Rows.Count;
				for (int iRowTarget = 0 ; nbRowTarget > iRowTarget ; ++iRowTarget) 
				{
					drTarget = inout_dtTarget.Rows[iRowTarget];
					
					if(drTarget.RowState != DataRowState.Deleted)
					{
						drSource = null;
						for(int i=0; i<adcPK.Length; i++)
						{
							// find source key value by target's key column name
							aRowKey[i] = ConvertValue(drTarget[adcPK[i].ColumnName], adcPK[i].DataType);
						}
						// Locate/add source row in target (DELETE ?)
						drSource = in_dtSource.Rows.Find(aRowKey);
						if (null == drSource)
						{
							rowsToDelete.Add(drTarget);
						} // eof delete case
					}
				}
				foreach(DataRow row in rowsToDelete)
				{
					if(row.RowState != DataRowState.Deleted)
					{
						object primaryKey = PrimaryKey(row)[0];
						row.Delete();
						changed = true;
						if(changeList != null)
						{
							changeList.Add(new KeyValuePair<object,DataMergeChange>(
								primaryKey, new DataMergeChange(null, DataRowState.Deleted)));
						}
					}
				}
			} // eof for iRowTarget
		} // eof in_bSourceIsFragment
		return changed;
	} // eof method
    private static void CopyOriginalRecordVersion(DataTable inout_dtTarget, DataRow drSource, DataRow drTarget)
    {
        drTarget.BeginEdit();
        CopyRecordValues(drSource, DataRowVersion.Original, drTarget, 
            false);
        drTarget.EndEdit();
        inout_dtTarget.Rows.Add(drTarget);
        drTarget.AcceptChanges();
    }
    public static bool CopyRecordValues(
        DataRow sourceRow, 
        DataRowVersion sourceVersion, 
        DataRow destinationRow,
        bool enforceNullValues)
	{
		var changed = false;
		var changes = new Hashtable();
		for(var i = 0; destinationRow.Table.Columns.Count > i; ++i)
		{
			if(destinationRow.Table.Columns[i].Expression != "")
			{
				continue;
			}
			var column = destinationRow.Table.Columns[i];
			var columnName = column.ColumnName;
			if(!sourceRow.Table.Columns.Contains(columnName))
			{
				continue;
			}
			// Unless we're enforcing null values
			// Skip all columns that have AllowNulls=false and are null in the source.
			// This may be the case when the destination has a default value for such a column.
			// Therefore we skip assigning null, because we would fail anyway.
			// Since this is a new row, the default value will be used.
			if(((destinationRow.RowState == DataRowState.Added)
			|| (destinationRow.RowState == DataRowState.Detached))
		    && (sourceRow[columnName, sourceVersion] == DBNull.Value)
		    && (sourceRow.Table.Columns[columnName].AllowDBNull == false)
		    && (enforceNullValues == false))
			{
				continue;
			}
			if(destinationRow[columnName]
			   .Equals(sourceRow[columnName, sourceVersion]))
			{
				continue;
			}
			changes[columnName] = ConvertValue(
				sourceRow[columnName, sourceVersion], column.DataType);
			changed = true;
		}
		foreach(DictionaryEntry entry in changes)
		{
			destinationRow[(string)entry.Key] = entry.Value;
		}
		return changed;
	}
	public static void UpdateOrigamSystemColumns(DataRow row, bool isNew, object profileId)
	{
		UpdateOrigamSystemColumns(row, isNew, profileId, null);
	}
	public static void UpdateOrigamSystemColumns(DataRow row, bool isNew, object profileId, Hashtable changedColumns)
	{
		DataTable table = row.Table;
		if(isNew)
		{
			if(table.Columns.Contains("RecordCreated") && row["RecordCreated"] == DBNull.Value)
			{
				DataColumn col = table.Columns["RecordCreated"];
				row[col] = DateTime.Now;
				if(changedColumns != null && !changedColumns.Contains(col.ExtendedProperties["Id"])) changedColumns.Add(col.ExtendedProperties["Id"], col);
			}
			if(table.Columns.Contains("RecordCreatedBy") && row["RecordCreatedBy"] == DBNull.Value & profileId != null)
			{
				DataColumn col = table.Columns["RecordCreatedBy"];
				row[col] = profileId;
				if(changedColumns != null && !changedColumns.Contains(col.ExtendedProperties["Id"])) changedColumns.Add(col.ExtendedProperties["Id"], col);
			}
		}
		else
		{
			if(table.Columns.Contains("RecordUpdated") & row.RowState != DataRowState.Added)
			{
				DataColumn col = table.Columns["RecordUpdated"];
				row[col] = DateTime.Now;
				if(changedColumns != null && !changedColumns.Contains(col.ExtendedProperties["Id"]) ) changedColumns.Add(col.ExtendedProperties["Id"], col);
			}
			if(table.Columns.Contains("RecordUpdatedBy") & row.RowState != DataRowState.Added & profileId != null)
			{
				DataColumn col = table.Columns["RecordUpdatedBy"];
				row[col] = profileId;
				if(changedColumns != null && !changedColumns.Contains(col.ExtendedProperties["Id"])) changedColumns.Add(col.ExtendedProperties["Id"], col);
			}
		}
	}
    /// <summary>
    /// Assigns a new GUID to a primary key of a row in case the data type
    /// is GUID. Otherwise does nothing.
    /// </summary>
    /// <param name="row"></param>
    public static void ApplyPrimaryKey(DataRow row)
    {
        foreach (DataColumn pkCol in row.Table.PrimaryKey)
        {
            if (pkCol.DataType == typeof(Guid))
            {
                row[pkCol] = Guid.NewGuid();
            }
        }
    }
    public static DataRow CreateRow(DataRow parentRow, DataTable newRowTable, DataRelation relation, object profileId)
	{
		DataRow row = newRowTable.NewRow();
		if(parentRow != null)
		{
			row.SetParentRow(parentRow);
		}
        ApplyPrimaryKey(row);
		UpdateOrigamSystemColumns(row, true, profileId);
		return row;
	}
	public static XmlContainer GetRowXml(IEnumerable<DataRow> rows, DataRowVersion version)
	{
		XmlDocument doc = new XmlDocument();
		XmlElement rowsElement = doc.CreateElement("rows");
		doc.AppendChild(rowsElement);
		foreach(DataRow row in rows)
		{
			XmlElement e = doc.CreateElement("row");
			rowsElement.AppendChild(e);
			GetRowXml(e, doc, row, version);
		}
		return new XmlContainer(doc);
	}
	public static XmlContainer GetRowXml(DataRow row, DataRowVersion version)
	{
		XmlDocument doc = new XmlDocument();
		XmlElement e = doc.CreateElement("row");
		doc.AppendChild(e);
		GetRowXml(e, doc, row, version);
		return  new XmlContainer(doc);
	}
    public static void MergeDataSetVerbose(DataSet mergeInDS, DataSet mergeFromDS)
    {
        try
        {
            mergeInDS.Merge(mergeFromDS);
        }
        catch (ConstraintException)
        {
            throw new ConstraintException(String.Format("MergeInDSErrors: `{0}', MergeFromDSErrors: `{1}'",
                DatasetTools.GetDatasetErrors(mergeInDS),
                DatasetTools.GetDatasetErrors(mergeFromDS)));
        }
    }
	private static void GetRowXml(XmlElement e, XmlDocument doc, DataRow row, DataRowVersion version)
	{
		if(version == DataRowVersion.Original
			&& (row.RowState == DataRowState.Detached || row.RowState == DataRowState.Added)) 
		{
			version = DataRowVersion.Default;
		}
		foreach(DataColumn col in row.Table.Columns)
		{
			object rowValue = row[col, version];
			if(col.Expression != null && col.Expression.ToUpper().StartsWith("PARENT."))
			{
				DataRowVersion parentVersion = version;
				if(parentVersion == DataRowVersion.Proposed)
				{
					parentVersion = DataRowVersion.Default;
				}
				else if(parentVersion == DataRowVersion.Original 
					&& (row.RowState == DataRowState.Detached || row.RowState == DataRowState.Added)) 
				{
					parentVersion = DataRowVersion.Default;
				}
				string parentColumnName = col.Expression.Substring(7);
				DataTable parentTable = row.Table.ParentRelations[row.Table.TableName].ParentTable;
				if(parentTable.Columns.Contains(parentColumnName))
				{
					rowValue = row.GetParentRow(row.Table.TableName)[parentColumnName, parentVersion];
				}
			}
			if(rowValue != DBNull.Value)
			{
				string val;
				if(rowValue is string)
				{
					val = (string)rowValue;
				}
				else if(rowValue is Guid)
				{
					val = rowValue.ToString();
				}
				else if(rowValue is int)
				{
					val = XmlConvert.ToString((int)rowValue);
				}
				else if(rowValue is long)
				{
					val = XmlConvert.ToString((long)rowValue);
				}
				else if(rowValue is decimal)
				{
					val = XmlConvert.ToString((decimal)rowValue);
				}
				else if(rowValue is bool)
				{
					val = XmlConvert.ToString((bool)rowValue);
				}
				else if(rowValue is byte)
				{
					val = XmlConvert.ToString((byte)rowValue);
				}
				else if(rowValue is Single)
				{
					val = XmlConvert.ToString((Single)rowValue);
				}
				else if(rowValue is DateTime)
				{
					val = XmlConvert.ToString((DateTime)rowValue);
				}
				else
				{
					val = rowValue.ToString();
				}
				switch(col.ColumnMapping)
				{
					case MappingType.Attribute:
						e.SetAttribute(col.ColumnName, val); 
						break;
					case MappingType.Element:
						XmlNode colElement = e.AppendChild(doc.CreateElement(col.ColumnName));
						colElement.InnerText = val;
						break;
				}
			}
		}
	}
	
	public static void GetDataSlice(DataSet target, IList<DataRow> rows)
	{
		GetDataSlice(target, rows, null, false, null);
	}
	
	public static void GetDataSlice(DataSet target, IList<DataRow> rows, 
        object profileId, bool copy, List<string> tablesToSkip)
	{
        if (tablesToSkip == null)
        {
            tablesToSkip = new List<string>();
        }
		target.EnforceConstraints = false;
		// parent
		DataRow parentRow = rows[0];
		while(parentRow.Table.ParentRelations.Count > 0)
		{
			DataRow newParent = parentRow.GetParentRow(
                parentRow.Table.ParentRelations[0]);
            if (newParent == null)
            {
                break;
            }
			// infinite loop on self-joins
            if (newParent.Equals(parentRow))
            {
                break;
            }
			parentRow = newParent;
			target.Tables[parentRow.Table.TableName].ImportRow(parentRow);
            // last parent will get all the parent's children until the current row
            if (!copy && parentRow.Table.ParentRelations.Count == 0)
            {
                var skipCurrent = new List<string>(tablesToSkip);
                skipCurrent.Add(rows[0].Table.TableName);
                ImportChildRows(parentRow, target, copy, skipCurrent);
            }
        }
        foreach (DataRow row in rows)
        {
            // current
            DataRow importedRow = ImportRow(target.Tables[row.Table.TableName], row);
            // child
            ImportChildRows(row, target, copy, tablesToSkip);
            if(copy)
            {
                LoadWriteOnlyData (importedRow);
                CopyChildRows(importedRow, profileId);
            }
        }
	}
	private static void ImportChildRows(DataRow row, DataSet target, bool onlyParentChildRelations, List<string> tablesToSkip)
	{
		foreach(DataRelation relation in row.Table.ChildRelations)
		{
			if(tablesToSkip.Contains(relation.ChildTable.TableName) == false && (onlyParentChildRelations == false | relation.Nested))
			{
				foreach(DataRow childRow in row.GetChildRows(relation))
				{
					ImportRow(target.Tables[childRow.Table.TableName], childRow);
					ImportChildRows(childRow, target, onlyParentChildRelations, tablesToSkip);
				}
			}
		}
	}
	private static void CopyChildRows(DataRow row, object profileId)
	{
		DataTable table = row.Table;
		foreach(DataColumn col in table.Columns)
		{
			if(col.ColumnName == "Id")
			{
				row[col] = Guid.NewGuid();
			}
			else if(col.ColumnName == "RecordCreated")
			{
				row[col] = DateTime.Now;
			}
			else if(col.ColumnName == "RecordUpdated")
			{
				row[col] = DBNull.Value;
			}
			else if(col.ColumnName == "RecordCreatedBy")
			{
				row[col] = profileId;
			}
			else if(col.ColumnName == "RecordUpdatedBy")
			{
				row[col] = DBNull.Value;
			}
			else if(col.ExtendedProperties.Contains("IsState") && (bool)col.ExtendedProperties["IsState"] == true)
			{
				row[col] = col.DefaultValue;
			}
			else if(col.ExtendedProperties.Contains("OnCopyAction"))
			{
				switch((OnCopyActionType)col.ExtendedProperties["OnCopyAction"])
				{
					case OnCopyActionType.Initialize:
						if(col.DefaultValue == null)
						{
							row[col] = DBNull.Value;
						}
						else
						{
							row[col] = col.DefaultValue;
						}
						break;
					case OnCopyActionType.PrependCopyText:
						if(row[col] is string)
						{
							row[col] = GetCopiedValue(row, col);
						}
						break;
				}
			}
		}
		row.AcceptChanges();
		foreach(DataRelation relation in table.ChildRelations)
		{
			foreach(DataRow childRow in row.GetChildRows(relation))
			{
				CopyChildRows(childRow, profileId);
			}
		}
	}

	private static string GetCopiedValue(DataRow row, DataColumn col)
	{
		string valueWithCopyPrefix = ResourceUtils.GetString("CopyPrefix") + (string)row[col];
		if (valueWithCopyPrefix.Length > col.MaxLength)
		{
			valueWithCopyPrefix = col.MaxLength > 3
				? valueWithCopyPrefix.Substring(0, col.MaxLength - 3) + "..."
				: "...".Substring(0, col.MaxLength);
		}

		return valueWithCopyPrefix;
	}

	private static void LoadWriteOnlyData(DataRow row)
	{
		DataTable table = row.Table;
		foreach(DataColumn col in table.Columns)
		{
			if(col.ExtendedProperties.Contains(Const.IsWriteOnlyAttribute) 
				&& (bool)col.ExtendedProperties[Const.IsWriteOnlyAttribute])
			{
				if (col.ExtendedProperties.Contains (Const.DefaultLookupIdAttribute)) {
					Guid lookupId = (Guid)col.ExtendedProperties [Const.DefaultLookupIdAttribute];
					IDataLookupService lookupService = ServiceManager.Services.GetService (typeof(IDataLookupService)) as IDataLookupService;
					object result = lookupService.GetDisplayText(lookupId, DatasetTools.PrimaryKey(row)[0], false, false, null);
					row [col] = result;
				} else {
					throw new Exception (string.Format("Cannot copy blob fields - no lookup specified and column is WriteOnly. Entity: {0}, Column: {1}.",
						table.TableName, col.ColumnName));
				}
			}
		}
		foreach(DataRelation relation in table.ChildRelations)
		{
			foreach(DataRow childRow in row.GetChildRows(relation))
			{
				LoadWriteOnlyData(childRow);
			}
		}
	}
	private static DataRow ImportRow(DataTable table, DataRow row)
	{
		if(! table.Rows.Contains(PrimaryKey(row)))
		{
			DataRow importedRow = table.NewRow();
			if(row.RowState == DataRowState.Modified)
			{
				CopyRecordValues(row, DataRowVersion.Original, importedRow,
					false);
				table.Rows.Add(importedRow);
				importedRow.AcceptChanges();
				importedRow.BeginEdit();
				CopyRecordValues(row, DataRowVersion.Default, importedRow,
					false);
				importedRow.EndEdit();
			}
			else
			{
				importedRow.BeginEdit();
				CopyRecordValues(row, DataRowVersion.Default, importedRow,
					false);
				table.Rows.Add(importedRow);
				importedRow.EndEdit();
			}
			if(row.RowState == DataRowState.Unchanged)
			{
				importedRow.AcceptChanges();
			}
			return importedRow;
		}
		return null;
	}
	public static DataSet CloneDataSet(DataSet dataset)
	{
		return CloneDataSet(dataset, true);
	}
	public static DataSet CloneDataSet(DataSet dataset, bool cloneExpressions)
	{
		DataSet result = new DataSet(dataset.DataSetName);
		result.BeginInit();
		result.Namespace = dataset.Namespace;
		result.Prefix = dataset.Prefix;
		result.Locale = dataset.Locale;
		result.CaseSensitive = dataset.CaseSensitive;
		foreach(DictionaryEntry entry in dataset.ExtendedProperties)
		{
			result.ExtendedProperties.Add(entry.Key, entry.Value);
		}
		foreach(DataTable table in dataset.Tables)
		{
			// don't clone expressions, we do them all later, because of table dependencies
			result.Tables.Add(CloneTable(table, false));
		}
		DataRelation[] relations = new DataRelation[dataset.Relations.Count];
		for(int i=0; i < relations.Length; i++)
		{
			DataRelation relation = dataset.Relations[i];
            DataColumn[] parentColumns = new DataColumn[relation.ParentColumns.Length];
			for(int j=0; j < relation.ParentColumns.Length; j++)
			{
                parentColumns[j] = result.Tables[relation.ParentTable.TableName].Columns[relation.ParentColumns[j].ColumnName];
			}
            DataColumn[] childColumns = new DataColumn[relation.ChildColumns.Length];
			for(int j=0; j < relation.ChildColumns.Length; j++)
			{
                childColumns[j] = result.Tables[relation.ChildTable.TableName].Columns[relation.ChildColumns[j].ColumnName];
			}
            DataRelation newRelation = new DataRelation(
                relation.RelationName, parentColumns,
                childColumns, false);
            newRelation.Nested = relation.Nested;
			relations[i] = newRelation;
		}
		result.Relations.AddRange(relations);
		// we call end init before setting expressions, because relations must be commited
		result.EndInit();
		// copy foreign key constraints
		foreach(DataRelation relation in dataset.Relations)
		{
			DataRelation newRelation = result.Relations[relation.RelationName];
			foreach(Constraint constr in relation.ChildTable.Constraints)
			{
				if(! newRelation.ChildTable.Constraints.Contains(constr.ConstraintName))	// skip self-join repeated constraints
				{
					ForeignKeyConstraint fkc = constr as ForeignKeyConstraint;
					if(fkc != null)
					{
						DataColumn[] parentColumns = new DataColumn[fkc.Columns.Length];
						for(int j = 0; j < fkc.Columns.Length; j++)
						{
							parentColumns[j] = result.Tables[fkc.Table.TableName].Columns[fkc.Columns[j].ColumnName];
						}
					
						DataColumn[] childColumns = new DataColumn[fkc.RelatedColumns.Length];
						for(int j = 0; j < fkc.RelatedColumns.Length; j++)
						{
							childColumns[j] = result.Tables[fkc.RelatedTable.TableName].Columns[fkc.RelatedColumns[j].ColumnName];
						}
						ForeignKeyConstraint newfkc = new ForeignKeyConstraint(fkc.ConstraintName, childColumns, parentColumns);
						newfkc.AcceptRejectRule = fkc.AcceptRejectRule;
						newfkc.UpdateRule = fkc.UpdateRule;
						newfkc.DeleteRule = fkc.DeleteRule;
					
						newRelation.ChildTable.Constraints.Add(newfkc);
					}
				}
			}
		}
		if(cloneExpressions)
		{
			// copy expressions after all tables and columns are ready
			foreach(DataTable table in dataset.Tables)
			{
				DataTable resultTable = result.Tables[table.TableName];
				foreach(DataColumn column in table.Columns)
				{
					resultTable.Columns[column.ColumnName].Expression = column.Expression;
				}
			}
		}
		return result;
	}
	public static DataTable CloneTable(DataTable table, bool cloneExpressions)
	{
		DataTable changedTable = new OrigamDataTable(table.TableName);
		changedTable.DisplayExpression = table.DisplayExpression;
		changedTable.BeginInit();
						
		// let's copy extended properties (there might be some interesting ones, like IsAuditingEnabled
		foreach(DictionaryEntry entry in table.ExtendedProperties)
		{
			changedTable.ExtendedProperties.Add(entry.Key, entry.Value);
		}
		Hashtable expressions = new Hashtable();
							
		foreach(DataColumn col in table.Columns)
		{
			DataColumn newColumn = new DataColumn(col.ColumnName, col.DataType);
			newColumn.ColumnMapping = col.ColumnMapping;
			newColumn.AllowDBNull = col.AllowDBNull;
			newColumn.AutoIncrement = col.AutoIncrement;
			newColumn.AutoIncrementSeed = col.AutoIncrementSeed;
			newColumn.AutoIncrementStep = col.AutoIncrementStep;
			newColumn.Caption = col.Caption;
			newColumn.DefaultValue = col.DefaultValue;
			newColumn.MaxLength = col.MaxLength;
			newColumn.Namespace = col.Namespace;
			newColumn.Prefix = col.Prefix;
			newColumn.Unique = col.Unique;
			if(cloneExpressions && col.Expression != "")
			{
				expressions.Add(newColumn, col.Expression);
			}
			// copy extended properties
			foreach(DictionaryEntry entry in col.ExtendedProperties)
			{
				newColumn.ExtendedProperties.Add(entry.Key, entry.Value);
			}
			changedTable.Columns.Add(newColumn);
		}
		if(cloneExpressions)
		{
			foreach(DictionaryEntry entry in expressions)
			{
				(entry.Key as DataColumn).Expression = (string)entry.Value;
			}
		}
		// copy the primary key
		DataColumn[] keys = new DataColumn[table.PrimaryKey.Length];
		for(int i = 0; i < table.PrimaryKey.Length; i++)
		{
			keys[i] = changedTable.Columns[table.PrimaryKey[i].ColumnName];
		}
		changedTable.PrimaryKey = keys;
		changedTable.EndInit();
		return changedTable;
	}
	
	public static DataRow CloneRow(DataRow queueRow)
	{
		DataSet oneRowDataSet = CloneDataSet(queueRow.Table.DataSet);
		GetDataSlice(oneRowDataSet, new List<DataRow> { queueRow });
		DataRow oneRow = oneRowDataSet.Tables[0].Rows[0];
		return oneRow;
	}
	public static void AddSortColumns(DataSet data)
	{
		foreach(DataTable table in data.Tables)
		{
			DataColumn[] columns = new DataColumn[table.Columns.Count];
			table.Columns.CopyTo(columns, 0);
			foreach(DataColumn tableColumn in columns)
			{
				if(tableColumn.ExtendedProperties.Contains(Const.DefaultLookupIdAttribute) || tableColumn.DataType == typeof(Guid))
				{
					DatasetTools.CreateSortColumnName(tableColumn.ColumnName, table);
				}
			}
		}
	}
	public static string SortColumnName(string originalColumnName)
	{
		return "___" + originalColumnName; //lookupId;
	}
	public static void CreateSortColumnName(string originalColumnName, DataTable table)
	{
		string lookupColumnName = DatasetTools.SortColumnName(originalColumnName);
		DataColumn lookupColumn = table.Columns.Add(lookupColumnName, typeof(string));
		lookupColumn.ColumnMapping = MappingType.Hidden;
		lookupColumn.ExtendedProperties.Add(Const.TemporaryColumnAttribute, originalColumnName);
//			lookupColumn.ExtendedProperties.Add(Const.TemporaryColumnLookupAttribute, lookupId);
		lookupColumn.ExtendedProperties.Add("Id", Guid.Empty);
	}
	public static bool CheckRowErrorRecursive(DataRow row, DataRow skipRow, bool includeChildErrorsInParent)
	{
		bool result = false;
		bool childErrors = false;
		// check all children
        if (row.RowState != DataRowState.Deleted && row.RowState != DataRowState.Detached)
		{
			foreach(DataRelation childRelation in row.Table.ChildRelations)
			{
				foreach(DataRow childRow in row.GetChildRows(childRelation))
				{
					// check recursion
					foreach(DataRelation parentRelation in row.Table.ParentRelations)
					{
						foreach(DataRow parentRow in row.GetParentRows(parentRelation))
						{
							if(parentRow.Equals(childRow))
							{
								// Recursion found - this row has been checked already.
								goto finish;
							}
						}
					}
					if(CheckRowErrorRecursive(childRow, skipRow, includeChildErrorsInParent)) result = true;
				}
			}
		}
		finish:
			childErrors = result;
		// check myself
		if(skipRow == null || (skipRow != null & skipRow != row))
		{
			if(CheckRowError(row)) result = true;
		}
		if(includeChildErrorsInParent && childErrors)
		{
#if DEBUG
			System.Diagnostics.Debug.WriteLine(row.HasVersion(DataRowVersion.Original), "RowVersion Original Before");
#endif
			if(row.RowError != "") row.RowError += "; ";
			row.RowError += ResourceUtils.GetString("ChildErrors");
#if DEBUG
			System.Diagnostics.Debug.WriteLine(row.HasVersion(DataRowVersion.Original), "RowVersion Original After");
#endif
		}
		// return
		return result;
	}
	public static void CheckRowErrorOfChangedRows(DataSet dataSet)
	{
		if (dataSet == null)
		{
			return;
		}
		foreach(DataTable table in dataSet.Tables)
		{
			foreach(DataRow row in table.Rows)
			{
				if (row.RowState != DataRowState.Unchanged)
				{
					CheckRowError(row);
				}
			}
		}
	}
	private static bool CheckRowError(DataRow row)
	{
        if (row.HasErrors)
        {
            row.ClearErrors();
        }
        if (row.RowState == DataRowState.Deleted 
            || row.RowState == DataRowState.Detached) return false;
		bool result = false;
		foreach(DataColumn col in row.Table.Columns)
		{
			if(col.ExtendedProperties.Contains("AllowNulls"))
			{
				if((bool)col.ExtendedProperties["AllowNulls"] == false 
                    && row[col] == DBNull.Value)
				{
					if(row.RowError != "") row.RowError += "; ";
					row.RowError += ResourceUtils.GetString("CantBeEmpty", 
                        (col.Caption == "" ? col.ColumnName : col.Caption));
					row.SetColumnError(col, ResourceUtils.GetString("FillPlease", 
                        (col.Caption == "" ? col.ColumnName : col.Caption)));
					result = true;
				}
			}
		}
		return result;
	}
	public static DataRow RootRow(DataRow childRow)
	{
		DataRow rootRow = childRow;
		while(rootRow != null && rootRow.Table.ParentRelations.Count > 0)
		{
			if(rootRow.Table.ParentRelations[0].ParentTable == rootRow.Table.ParentRelations[0].ChildTable)
			{
				// self-join
				if(rootRow.Table.ParentRelations.Count > 1)
				{
					// try another relation
					DataRow parent = rootRow.GetParentRow(rootRow.Table.ParentRelations[1]);
                    if (parent == null)
                    {
                        break;
                    }
                    rootRow = parent;
                    
				}
				else
				{
					// or quit here
					break;
				}
			}
			else
			{
				DataRow parent = rootRow.GetParentRow(rootRow.Table.ParentRelations[0]);
                if (parent == null)
                {
                    break;
                }
                rootRow = parent;
			}
		}
		return rootRow;
	}
	public static DataRowVersion GetRowVersion(DataRow row, bool originalData)
	{
		DataRowVersion version = DataRowVersion.Default;
		if(originalData)
		{
			switch(row.RowState)
			{
				case DataRowState.Added:
					version = DataRowVersion.Current;
					break;
				case DataRowState.Deleted:
					version = DataRowVersion.Original;
					break;
				case DataRowState.Modified:
					version = DataRowVersion.Original;
					break;
			}
		}
		return version;
	}
	public static Hashtable RetrieveParameters(Hashtable parameterMappings, IList<DataRow> rows)
	{
		return RetrieveParameters(parameterMappings, rows, new List<string>());
	}
    public static Hashtable RetrieveParameters(Hashtable parameterMappings,
        IList<DataRow> rows, List<string> originalDataParameters)
    {
        DataSet fullData = null;
        if (rows != null && rows.Count > 0)
        {
            fullData = rows[0].Table.DataSet;
        }
        return RetrieveParameters(parameterMappings, rows, originalDataParameters,
            fullData);
    }
    public static Hashtable RetrieveParameters(Hashtable parameterMappings, 
        IList<DataRow> rows, List<string> originalDataParameters, DataSet fullData)
	{
		Hashtable result = new Hashtable();
		foreach(DictionaryEntry entry in parameterMappings)
		{
            string parameterName = (string)entry.Key;
            string columnName = (string)entry.Value; ;
            if (columnName != null && columnName.StartsWith("'"))
            {
                // not a field name but a constant, we just return it
                result.Add(parameterName, columnName.Trim("'".ToCharArray()));
                columnName = null;
            }
			bool originalData = (originalDataParameters.Contains(parameterName));
			if(columnName != "/" && originalData)
			{
				throw new Exception("Original data can be passed only when passing the whole document using '/'.");
			}
            if (columnName == "/")
			{
				DataSet copy = fullData.Copy();
				if(originalData)
				{
					copy.RejectChanges();
				}
				result.Add(parameterName, DataDocumentFactory.New(copy));
				columnName = null;
			}
            else
            {
                if ((rows == null) || (rows.Count == 0))
                {
                    // if the parameter is not a constant and there are no rows
                    // we do no even try to process it
                    continue;
                }
                DataRow row = rows[0];
				if (columnName != null && columnName.StartsWith("{"))
                {
                    string propertyName = GetExtendedPropertyName(columnName, row);
                    result.Add(parameterName, row.Table.ExtendedProperties[propertyName]);
					columnName = null;
                }
                else if (columnName == ".")
                {
                    DataSet slice = fullData.Clone();
                    GetDataSlice(slice, rows);
                    result.Add(parameterName, DataDocumentFactory.New(slice));
                    columnName = null;
                }
                else if (columnName != null && columnName != "" && columnName.Substring(0, 1) == "/")
                {
                    string[] relations = columnName.Substring(1).Split(".".ToCharArray());
                    DataTable currentTable = fullData.Tables[relations[0]];
                    if (currentTable == null)
                    {
                        throw new Exception(ResourceUtils.GetString("TableNotFound", relations[0]));
                    }
                    for (int i = 1; i < relations.Length - 1; i++)
                    {
                        currentTable = currentTable.ChildRelations[relations[i]].ChildTable;
                        if (currentTable == null)
                        {
                            throw new Exception(ResourceUtils.GetString("TableNotFound", relations[i]));
                        }
                    }
                    if (currentTable.Rows.Count == 0)
                    {
                        throw new Exception(ResourceUtils.GetString("NoRowsInTable", currentTable.TableName));
                    }
                    columnName = relations[relations.Length - 1];
                    row = currentTable.Rows[0];
                }
                else if (columnName != null && columnName.IndexOf(".") > 0)
                {
                    string[] relations = columnName.Split(".".ToCharArray());
                    for (int i = 0; i < relations.Length - 1; i++)
                    {
                        row = row.GetParentRow(relations[i]);
                        if (row == null)
                        {
                            throw new IndexOutOfRangeException(ResourceUtils.GetString("RelationNotFound", relations[i]));
                        }
                    }
                    columnName = relations[relations.Length - 1];
                }
                if (columnName != null && columnName != "")
                {
                    columnName = columnName.Trim();
                    if (!row.Table.Columns.Contains(columnName)) throw new ArgumentOutOfRangeException("ColumnName", columnName, ResourceUtils.GetString("MappedColumnNotFound", parameterName));
                    result.Add(parameterName, RetrieveValue(row, columnName));
                }
            }
        }
		return result;
	}
    private static string GetExtendedPropertyName(string name, DataRow row)
    {
        string trimmed = name.Trim();
        if (trimmed.Substring(trimmed.Length - 1) != "}")
        {
            throw new Exception("Expression must be enclosed in curly brackets. Invalid expression: " + trimmed);
        }
        string propertyName = trimmed.Substring(1, trimmed.Length - 2);
        if (!row.Table.ExtendedProperties.Contains(propertyName))
        {
            throw new Exception("Property does not exist: " + trimmed);
        }
        return propertyName;
    }
    public static bool IsAliasedColumn(DataColumn[] columns, Guid fieldId)
	{
		foreach(DataColumn col in columns)
		{
			if(col.ExtendedProperties.Contains("Id"))
			{
				if((Guid)col.ExtendedProperties["Id"] == fieldId)
				{
					return true;
				}
			}
		}
		return false;
	}
	
	public static object ConvertValue(object value, Type targetType)
	{
		if(value == null || value == DBNull.Value) return DBNull.Value;
		if(value.GetType().Equals(targetType)) return value;
		if(targetType == typeof(string))
		{
			if(value is int)
			{
				return XmlConvert.ToString((int)value);
			}
			else if(value is decimal)
			{
				return XmlConvert.ToString((decimal)value);
			}
			else if(value is bool)
			{
				return XmlConvert.ToString((bool)value);
			}
			else if(value is Guid)
			{
				return XmlConvert.ToString((Guid)value);
			}
			else if(value is DateTime)
			{
				return XmlConvert.ToString((DateTime)value);
			}
			else
			{
				return value.ToString();
			}
		}
		else if (targetType == typeof(Guid))
		{
			return new Guid(value.ToString());
		}
		else if (targetType == typeof(DateTime))
		{
			return XmlConvert.ToDateTime(value.ToString());
		}
		else if (targetType == typeof(bool))
		{
			return XmlConvert.ToBoolean(value.ToString());
		}
		else if (targetType == typeof(Int32))
		{
			return XmlConvert.ToInt32(value.ToString());
		}
		else if (targetType == typeof(double))
		{
			return XmlConvert.ToDouble(value.ToString());
		}
		else if (targetType == typeof(Int64))
		{
			return XmlConvert.ToInt64(value.ToString());
		}
		else if (targetType == typeof(decimal))
		{
			return XmlConvert.ToDecimal(value.ToString());
		}
		else
		{
			return Convert.ChangeType(value, targetType);
		}
	}
    public static object RetrieveValue(DataRow row, string columnName)
    {
        object value = row[columnName];
        DataColumn column = row.Table.Columns[columnName];
        if (column.ExtendedProperties.Contains(Const.ArrayRelation))
        {
            string childColumnName = (string)column.ExtendedProperties[Const.ArrayRelationField];
            var list = new ArrayList();
            foreach (DataRow childRow in
                row.GetChildRows((string)column.ExtendedProperties[Const.ArrayRelation]))
            {
                list.Add(childRow[childColumnName]);
            }
            value = list;
        }
        return value;
    }
    public static IDictionary<DataColumn, string> RemoveExpressions(DataSet data, bool makeReadWrite = false)
    {
        Dictionary<DataColumn, string> result = new Dictionary<DataColumn, string>();
        foreach (DataTable table in data.Tables)
        {
            foreach (DataColumn column in table.Columns)
            {
                if (column.Expression != "")
                {
                    result.Add(column, column.Expression);
                    column.Expression = "";
					if (makeReadWrite)
					{
						column.ReadOnly = false;
					}
                }
            }
        }
        return result;
    }
    public static void SetExpressions(IDictionary<DataColumn, string> expressions)
    {
        foreach (KeyValuePair<DataColumn, string> item in expressions)
        {
            item.Key.Expression = item.Value;
			item.Key.ReadOnly = true;
        }
    }
    public static bool IsParameterViableAsResultContext(
        EntityUIActionParameterMapping mapping)
    {
        return (mapping.Type == EntityUIActionParameterMappingType.Current)
            && ((mapping.Field == "/") || (mapping.Field == "."));
    }
    public static bool HasDataSetRules(
        DataSet dataSet, DataStructureRuleSet ruleSet)
    {
        // has ruleset
        if(ruleSet != null) return true;
        // has some lookup fields that are processed (looked up on changes) 
        // by the rule engine
        if(dataSet != null)
        {
            foreach(DataTable table in dataSet.Tables)
            {
                foreach(DataColumn column in table.Columns)
                {
                    if(column.ExtendedProperties.Contains(
                        Const.OriginalFieldId))
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }
    /// <summary>
    /// Compares two datasets and returns the top-level deleted rows (in case e.g. whole hierarchy
    /// of parent-child rows is deleted) together with their non-deleted parent rows.
    /// E.g. SalesOrder -> SalesDelivery -> SalesDeliveryDetail
    /// If SalesDelivery is deleted, also SalesDeliveryDetail is deleted.
    /// The function will return SalesDelivery row as deleted row and SalesOrder row as parent.
    /// </summary>
    /// <param name="sourceData"></param>
    /// <param name="targetData"></param>
    /// <returns></returns>
    public static Dictionary<DataRow, List<DataRow>> GetDeletedRows(DataSet sourceData, DataSet targetData)
    {
        Dictionary<DataRow, List<DataRow>> deletedRowsParents = new Dictionary<DataRow, List<DataRow>>();
        foreach (DataTable targetTable in targetData.Tables)
        {
            if (sourceData.Tables.Contains(targetTable.TableName))
            {
                DataTable sourceTable = sourceData.Tables[targetTable.TableName];
                foreach (DataRow targetRow in targetTable.Rows)
                {
                    if (!RowExistsInOtherTable(sourceTable, targetRow))
                    {
                        DataRow deletedRow = targetRow;
                        DataRow notDeletedParent = null;
                        while (notDeletedParent == null)
                        {
                            if (deletedRow.Table.ParentRelations.Count > 0)
                            {
                                DataRow parent = deletedRow.GetParentRow(deletedRow.Table.ParentRelations[0],
                                    deletedRow.RowState == DataRowState.Deleted ? DataRowVersion.Original : DataRowVersion.Default);
                                if (RowExistsInOtherTable(sourceTable, parent))
                                {
                                    notDeletedParent = parent;
                                }
                                else
                                {
                                    deletedRow = parent;
                                }
                            }
                            else
                            {
                                break;
                            }
                        }
                        if (!deletedRowsParents.ContainsKey(deletedRow))
                        {
                            List<DataRow> parents = new List<DataRow>();
                            if (notDeletedParent != null)
                            {
                                parents.Add(notDeletedParent);
                                while (notDeletedParent.Table.ParentRelations.Count > 0)
                                {
                                    notDeletedParent = notDeletedParent.GetParentRow(notDeletedParent.Table.ParentRelations[0]);
                                    parents.Add(notDeletedParent);
                                }
                            }
                            deletedRowsParents.Add(deletedRow, parents);
                        }
                    }
                }
            }
        }
        return deletedRowsParents;
    }
    private static bool RowExistsInOtherTable(
        DataTable sourceTable, DataRow targetRow)
    {
        return sourceTable.Rows.Find(DatasetTools.PrimaryKey(targetRow)) 
            != null;
    }
}
