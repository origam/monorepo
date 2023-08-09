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
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Xml;
using Origam.DA;
using Origam.DA.Service;
using Origam.Gui;
using Origam.Schema;
using Origam.Schema.EntityModel;
using Origam.Schema.GuiModel;
using Origam.Schema.MenuModel;
using Origam.Schema.WorkflowModel;
using Origam.Workbench.Services;
using Origam.Workflow;
using core = Origam.Workbench.Services.CoreServices;

namespace Origam.OrigamEngine.ModelXmlBuilders
{
    public class XmlOutput
    {
        public XmlDocument Document { get; set; }
        public HashSet<Guid> ContainedLookups { get; set; } = new HashSet<Guid>();
    }

    /// <summary>
	/// Summary description for FormXmlBuilder.
	/// </summary>
	public class FormXmlBuilder
	{
		public const string WORKFLOW_FINISHED_FORMID = "C4E5DE43-69A4-40e6-9F7F-ED2AF8692429";
        private const int GENERIC_FIELD_HEIGHT = 20;
        private const int GENERIC_FIELD_VERTICAL_SPACE = 2;

        public static XmlOutput GetXml(Guid menuId)
		{
			IPersistenceService persistence = ServiceManager.Services.GetService(
                typeof(IPersistenceService)) as IPersistenceService;

			FormReferenceMenuItem menuItem = persistence.SchemaProvider.RetrieveInstance(
                typeof(FormReferenceMenuItem), new ModelElementKey(menuId)) as FormReferenceMenuItem;

			bool readOnly = FormTools.IsFormMenuReadOnly(menuItem);
			return GetXml(menuItem.Screen, menuItem.DisplayName, 
                menuItem.ListDataStructure == null, menuItem.Id, 
                menuItem.Screen.DataStructure, readOnly, menuItem.SelectionChangeEntity);
		}

		public static XmlDocument GetXml(Guid formId, string name, bool isPreloaded, 
            Guid menuId, string message, Guid structureId, bool forceReadOnly, 
            string confirmSelectionChangeEntity)
		{
			if(formId == new Guid(WORKFLOW_FINISHED_FORMID))
			{
				return GetWorkflowFinishedXml(name, menuId, message);
			}
			else
			{
				IPersistenceService persistence = ServiceManager.Services.GetService(
                    typeof(IPersistenceService)) as IPersistenceService;
				FormControlSet item = persistence.SchemaProvider.RetrieveInstance(
                    typeof(FormControlSet), new ModelElementKey(formId)) as FormControlSet;
				DataStructure structure = persistence.SchemaProvider.RetrieveInstance(
                    typeof(DataStructure), new ModelElementKey(structureId)) as DataStructure;

				return GetXml(item, name, isPreloaded, menuId, structure, forceReadOnly,
                    confirmSelectionChangeEntity).Document;
			}
		}

		public static XmlDocument GetXmlFromPanel(Guid panelId, string name, Guid menuId)
		{
			return GetXmlFromPanel(panelId, name, menuId, panelId, true);
		}
		
		public static XmlDocument GetXmlFromPanel(Guid panelId, string name, Guid menuId, 
            Guid instanceId, bool forceHideNavigationPanel)
		{
			IPersistenceService persistence = ServiceManager.Services.GetService(
                typeof(IPersistenceService)) as IPersistenceService;
			PanelControlSet panel = persistence.SchemaProvider.RetrieveInstance(
                typeof(PanelControlSet), new ModelElementKey(panelId)) as PanelControlSet;

			FormControlSet form = new FormControlSet();

			form.PersistenceProvider = persistence.SchemaProvider;
			ControlSetItem control = form.NewItem<ControlSetItem>( 
                form.SchemaExtensionId, form.Group);
			control.PrimaryKey = new ModelElementKey(instanceId);

			control.ControlItem = panel.PanelControl;

			foreach(PropertyValueItem panelProperty in
			    FormTools.GetItemFromControlSet(panel).ChildItemsByType(
                    PropertyValueItem.CategoryConst))
			{
				PropertyValueItem copy = panelProperty.Clone() as PropertyValueItem;
				
				if(forceHideNavigationPanel && copy.ControlPropertyItem.Name == "HideNavigationPanel") 
				{
					copy.BoolValue = true;
				}

				copy.ParentItem = control;
				control.ChildItems.Add(copy);
			}

			PropertyValueItem dataMemberProperty = control
				.NewItem<PropertyValueItem>(control.SchemaExtensionId, null);
			dataMemberProperty.Name = "DataMember";
			dataMemberProperty.Value = panel.DataEntity.Name;
			dataMemberProperty.ControlPropertyItem = 
                control.ControlItem.GetChildByName("DataMember") as ControlPropertyItem;

			DatasetGenerator gen = new DatasetGenerator(true);
			return GetXml(form, gen.CreateDataSet(panel.DataEntity), name, true, menuId, false, "").Document;
		}

		internal static XmlDocument GetWindowBaseXml(
			string name, Guid menuId, int autoRefreshInterval, 
            bool refreshOnFocus, bool autoSaveOnListRecordChange,
            bool requestSaveAfterUpdate)
		{
			XmlDocument doc = new XmlDocument();
			doc.LoadXml("<Window xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\"/>");
			// <Window>
			XmlElement windowElement = doc.FirstChild as XmlElement;
			doc.AppendChild(windowElement);
			windowElement.SetAttribute("Title", name);
			windowElement.SetAttribute("MenuId", menuId.ToString());
			windowElement.SetAttribute("ShowInfoPanel", "false");
			windowElement.SetAttribute("AutoRefreshInterval", XmlConvert.ToString(autoRefreshInterval));
			windowElement.SetAttribute("CacheOnClient", "true");
			if(refreshOnFocus || (autoRefreshInterval > 0))
			{
				windowElement.SetAttribute("RefreshOnFocus", "true");
			}
            windowElement.SetAttribute("autoSaveOnListRecordChange", XmlConvert.ToString(autoSaveOnListRecordChange));
            windowElement.SetAttribute("RequestSaveAfterUpdate", XmlConvert.ToString(requestSaveAfterUpdate));
			IPersistenceService persistenceService = ServiceManager.Services.GetService(
                typeof(IPersistenceService)) as IPersistenceService;
            FormReferenceMenuItem formReferenceMenuItem = persistenceService
                .SchemaProvider.RetrieveInstance(typeof(FormReferenceMenuItem), 
                new ModelElementKey(menuId)) as FormReferenceMenuItem;
            if((formReferenceMenuItem != null) 
            && (formReferenceMenuItem.DynamicFormLabelEntity != null) 
            && !string.IsNullOrEmpty(
                formReferenceMenuItem.DynamicFormLabelField))
            {
                windowElement.SetAttribute("DynamicFormLabelSource",
                    formReferenceMenuItem.DynamicFormLabelEntity.Name 
                    + "." + formReferenceMenuItem.DynamicFormLabelField);
            }
            // <Window><DataSources>
            XmlElement dataSourcesElement = doc.CreateElement("DataSources");
			windowElement.AppendChild(dataSourcesElement);
			// <Window><ComponentBindings>
			XmlElement bindingsElement = doc.CreateElement("ComponentBindings");
			windowElement.AppendChild(bindingsElement);
			// <Window><UIRoot>
			XmlElement uiRootElement = doc.CreateElement("UIRoot");
			windowElement.AppendChild(uiRootElement);
			return doc;
		}

		private static XmlDocument GetWorkflowFinishedXml(string name, Guid menuId, string message)
		{
			XmlDocument doc = GetWindowBaseXml(name, menuId, 0, false, false, false);
			XmlElement windowElement = WindowElement(doc);
			XmlElement uiRootElement = UIRootElement(windowElement);

			windowElement.SetAttribute("CacheOnClient", "false");
			uiRootElement.SetAttribute("type", "UIElement", "http://www.w3.org/2001/XMLSchema-instance");
			uiRootElement.SetAttribute("Type", "WorkflowFinishedPanel");
			uiRootElement.SetAttribute("Message", message);
			uiRootElement.SetAttribute("showWorkflowCloseButton", "true");
			uiRootElement.SetAttribute("showWorkflowRepeatButton", "true");

			return doc;
		}

		public static XmlOutput GetXml(FormControlSet item, string name, bool isPreloaded, Guid menuId, DataStructure structure, bool forceReadOnly, string confirmSelectionChangeEntity)
		{
			DatasetGenerator gen = new DatasetGenerator(true);
			return GetXml(item, gen.CreateDataSet(structure), 
				name, isPreloaded, menuId, 
				forceReadOnly, confirmSelectionChangeEntity, structure);
		}

		public static XmlElement CreateDataSourceField(XmlDocument doc, string name, int index)
		{
			XmlElement dataSourceFieldElement = doc.CreateElement("Field");
			dataSourceFieldElement.SetAttribute("Name", name);
			dataSourceFieldElement.SetAttribute("Index", index.ToString());

			return dataSourceFieldElement;
		}

		private static void RenderDataSources(XmlElement windowElement, Hashtable dataSources)
		{
			XmlElement dataSourcesElement = DataSourcesElement(windowElement);

			foreach(DictionaryEntry entry in dataSources)
			{
				DataTable table = (DataTable)entry.Value;

				string entityName = (string)entry.Key;
				if (table.PrimaryKey.Length == 0)
				{
					throw new ArgumentException($"Cannot render data source into xml, because the source table \"{table.TableName}\" does not have a primary key.");
				}

				string identifier = table.PrimaryKey[0].ColumnName;
				string lookupCacheKey = DatabaseTableName(table);
			    string dataStructureEntityId = table.ExtendedProperties["Id"]?.ToString();

				XmlElement dataSourceElement =
				    AddDataSourceElement(dataSourcesElement, entityName,  identifier, lookupCacheKey, dataStructureEntityId);

				foreach(DataColumn c in table.Columns)
				{
					dataSourceElement.AppendChild(CreateDataSourceField(dataSourceElement.OwnerDocument, c.ColumnName, c.Ordinal));
				}

				dataSourceElement.AppendChild(CreateDataSourceField(dataSourceElement.OwnerDocument, "__Errors", table.Columns.Count));
			}
		}

		public static string DatabaseTableName(DataTable table)
		{
			IPersistenceService ps = ServiceManager.Services.GetService(typeof(IPersistenceService)) as IPersistenceService;
			TableMappingItem tableMapping = ps.SchemaProvider.RetrieveInstance(typeof(TableMappingItem), new ModelElementKey((Guid)table.ExtendedProperties["EntityId"])) as TableMappingItem;
			if(tableMapping != null)
			{
				return tableMapping.MappedObjectName;
			}
			else
			{
				return null;
			}
		}

		public static XmlElement AddDataSourceElement(XmlElement dataSourcesElement, string entity, 
		    string identifier, string lookupCacheKey, string dataStructureEntityId)
		{
			XmlElement dataSourceElement = dataSourcesElement.OwnerDocument.CreateElement("DataSource");
			dataSourcesElement.AppendChild(dataSourceElement);

			dataSourceElement.SetAttribute("Entity", entity);
		    if (!string.IsNullOrEmpty(dataStructureEntityId))
		    {
		        dataSourceElement.SetAttribute("DataStructureEntityId", dataStructureEntityId);
            }
            dataSourceElement.SetAttribute("Identifier", identifier);
			if(lookupCacheKey != null)
			{
				dataSourceElement.SetAttribute("LookupCacheKey", lookupCacheKey);
			}

			return dataSourceElement;
		}

		internal static XmlElement WindowElement(XmlDocument doc)
		{
			return doc.FirstChild as XmlElement;
		}

		internal static XmlElement UIRootElement(XmlElement windowElement)
		{
			return windowElement.SelectSingleNode("UIRoot") as XmlElement;
		}

		internal static XmlElement DataSourcesElement(XmlElement windowElement)
		{
			return windowElement.SelectSingleNode("DataSources") as XmlElement;
		}

		internal static XmlElement ComponentBindingsElement(XmlElement windowElement)
		{
			return windowElement.SelectSingleNode("ComponentBindings") as XmlElement;
		}

		public static XmlDocument GetXml(WorkQueueClass wqc, DataSet dataset, string name, Guid queueId)
		{
			Hashtable dataSources = new Hashtable();
			OrigamSettings settings = ConfigurationManager.GetActiveConfiguration();
			DataTable table = dataset.Tables["WorkQueueEntry"];

			QueryParameterCollection pms = new QueryParameterCollection();
			pms.Add(new QueryParameter("WorkQueueCommand_parWorkQueueId", queueId));
			DataSet commands = core.DataService.Instance.LoadData(new Guid("1d33b667-ca76-4aaa-a47d-0e404ed6f8a6"), new Guid("421aec03-1eec-43f9-b0bb-17cfc24510a0"), Guid.Empty, Guid.Empty, null, pms);

			ArrayList commandRows = new ArrayList();
            IOrigamAuthorizationProvider auth = SecurityManager.GetAuthorizationProvider();
			foreach (DataRow cmdRow in commands.Tables["WorkQueueCommand"].Rows)
			{
				if (cmdRow.IsNull("Roles") || !auth.Authorize(SecurityManager.CurrentPrincipal, (string)cmdRow["Roles"]))
				{
					continue;
				}
				commandRows.Add(cmdRow);
			}
			bool showCheckboxes = commandRows.Count > 0;

			// Window
			XmlDocument doc = GetWindowBaseXml(
				name, Guid.Empty, settings.WorkQueueListRefreshPeriod, false, false, false);
			XmlElement windowElement = WindowElement(doc);
			XmlElement uiRootElement = UIRootElement(windowElement);
			XmlElement bindingsElement = ComponentBindingsElement(windowElement);

			windowElement.SetAttribute("SuppressSave", "true");
			windowElement.SetAttribute("CacheOnClient", "false");

			SplitPanelBuilder.Build(uiRootElement, SplitPanelOrientation.Horizontal, false);
			uiRootElement.SetAttribute("ModelInstanceId", "52DEFCEA-587C-47e0-97F5-3590B6AC492F");
			XmlElement children = doc.CreateElement("UIChildren");
			uiRootElement.AppendChild(children);

			XmlElement listElement = doc.CreateElement("UIElement");
			children.AppendChild(listElement);

			// Panel
			UIElementRenderData renderData = new UIElementRenderData();
			renderData.PanelTitle = ResourceUtils.GetString("WorkQueueFromTitle");
			renderData.IsGridVisible = true;
			renderData.DataMember = "WorkQueueEntry";
			AsPanelBuilder.Build(listElement, renderData, queueId.ToString(), "queuePanel1", table, dataSources, 
				table.PrimaryKey[0].ColumnName, showCheckboxes, Guid.Empty, false);

			listElement.SetAttribute("Id", "queuePanel1");
			listElement.SetAttribute("ModelInstanceId", queueId.ToString());
			listElement.SetAttribute("IsRootGrid", XmlConvert.ToString(true));
			listElement.SetAttribute("IsRootEntity", XmlConvert.ToString(true));
			listElement.SetAttribute("IsPreloaded", XmlConvert.ToString(true));

			XmlElement formRootElement = AsPanelBuilder.FormRootElement(listElement);
			XmlElement propertiesElement = AsPanelBuilder.PropertiesElement(listElement);
			XmlElement actionsElement = AsPanelBuilder.ActionsElement(listElement);
			
			XmlElement propertyNamesElement = doc.CreateElement("PropertyNames");
			formRootElement.AppendChild(propertyNamesElement);

			DataStructureColumn memoColumn = null;

			// Panel controls
			int lastPos = 5;
			ArrayList mappedColumns = wqc.ChildItemsByType(WorkQueueClassEntityMapping.CategoryConst);
			mappedColumns.Sort();

			DataStructureEntity entity = wqc.WorkQueueStructure.Entities[0] as DataStructureEntity;

			foreach(WorkQueueClassEntityMapping mapping in mappedColumns)
			{
				// don't add RecordCreated twice
				if(mapping.Name == "RecordCreated") 
				{
					continue;
				}
				AddColumn(entity, mapping.Name, ref memoColumn, 
					ref lastPos, propertiesElement,	propertyNamesElement, table, mapping.FormatPattern);
			}

			AddColumn(entity, "IsLocked", ref memoColumn, 
				ref lastPos, propertiesElement,	propertyNamesElement, table, null);
			AddColumn(entity, "refLockedByBusinessPartnerId", ref memoColumn, 
				ref lastPos, propertiesElement,	propertyNamesElement, table, null);
			AddColumn(entity, "ErrorText", ref memoColumn, 
				ref lastPos, propertiesElement,	propertyNamesElement, table, null);
			AddColumn(entity, "RecordCreated", ref memoColumn,
				ref lastPos, propertiesElement, propertyNamesElement, table, null);

			SetUserConfig(doc, listElement, wqc.DefaultPanelConfiguration, queueId, Guid.Empty);

			if(memoColumn != null)
			{
				XmlElement memoElement = doc.CreateElement("UIElement");
				children.AppendChild(memoElement);

				UIElementRenderData memoRenderData = new UIElementRenderData();
				memoRenderData.DataMember = "WorkQueueEntry";
				memoRenderData.HideNavigationPanel = true;
				memoRenderData.PanelTitle = memoColumn.Caption;
				AsPanelBuilder.Build(memoElement, memoRenderData, queueId.ToString(), "memoPanel1",
					table, dataSources, table.PrimaryKey[0].ColumnName, false, Guid.Empty, false);

				memoElement.SetAttribute("Id", "memoPanel");
				memoElement.SetAttribute("ModelInstanceId", "65DF44F9-C050-4554-AD9A-896445314279");
				memoElement.SetAttribute("IsRootGrid", XmlConvert.ToString(false));
				memoElement.SetAttribute("IsRootEntity", XmlConvert.ToString(true));
				memoElement.SetAttribute("IsPreloaded", XmlConvert.ToString(true));
				memoElement.SetAttribute("ParentId", queueId.ToString());
				memoElement.SetAttribute("ParentEntityName", "WorkQueueEntry");

				XmlElement filterExpressionsElement = doc.CreateElement("FilterExpressions");
				memoElement.AppendChild(filterExpressionsElement);

				foreach(DataColumn col in table.PrimaryKey)
				{
					XmlElement filterElement = doc.CreateElement("FilterExpression");
					filterExpressionsElement.AppendChild(filterElement);
					filterElement.SetAttribute("ParentProperty", col.ColumnName);
					filterElement.SetAttribute("ItemProperty", col.ColumnName);
				}

				XmlElement memoFormRootElement = AsPanelBuilder.FormRootElement(memoElement);
				XmlElement memoPropertiesElement = AsPanelBuilder.PropertiesElement(memoElement);
			
				XmlElement memoPropertyNamesElement = doc.CreateElement("PropertyNames");
				memoFormRootElement.AppendChild(memoPropertyNamesElement);

				XmlElement propertyElement = AsPanelPropertyBuilder.CreateProperty(
					propertiesElement: memoPropertiesElement,
					propertyNamesElement: memoPropertyNamesElement, 
					modelId: memoColumn.Id, 
					bindingMember: memoColumn.Name, 
					caption: "", 
					gridCaption: null, 
					table: table,
					readOnly: true, 
					left: 0, 
					top: 0, 
					width: 100, 
					height: 16, 
					captionLength: 100, 
					captionPosition: "None", 
					gridColumnWidth: "500", 
					style: null, 
					tabIndex: null,
					fieldType: memoColumn.Field.FieldType
					);
				TextBoxBuildDefinition buildDefinition = new TextBoxBuildDefinition(OrigamDataType.Memo);
				buildDefinition.Dock = "Fill";
				buildDefinition.Multiline = true;
				TextBoxBuilder.Build(propertyElement, buildDefinition);

				// binding from the parent grid to the memo grid (same entity)
				CreateComponentBinding(doc, bindingsElement, queueId.ToString(), "Id", "WorkQueueEntry", "65DF44F9-C050-4554-AD9A-896445314279", "Id", "WorkQueueEntry", false);
			}

			IParameterService parameterService = ServiceManager.Services.GetService(typeof(IParameterService)) as IParameterService;

			foreach(DataRow cmdRow in commandRows)
			{
				Hashtable cmdParams = new Hashtable();
				string confirmationMessage = null;

				if((Guid)cmdRow["refWorkQueueCommandTypeId"] == (Guid)parameterService.GetParameterValue("WorkQueueCommandType_WorkQueueClassCommand"))
				{
					WorkQueueWorkflowCommand cmd = wqc.GetCommand((string)cmdRow["Command"]);
                    AsPanelActionButtonBuilder.Build(actionsElement, PanelActionType.QueueAction, cmd.Mode, 
						cmd.Placement, cmdRow["Id"].ToString(), "", (string)cmdRow["Text"], 
						(cmd.ButtonIcon == null ? null : cmd.ButtonIcon.Name), 
						(bool)cmdRow["IsDefault"], cmdParams, confirmationMessage);
				}
				else
				{
					string iconName = "";

					if((Guid)cmdRow["refWorkQueueCommandTypeId"] == (Guid)parameterService.GetParameterValue("WorkQueueCommandType_Remove"))
					{
						iconName = "queue_remove.png";
						confirmationMessage = ResourceUtils.GetString("WorkQueueRemoveConfirmationMessage");
					}
					else if((Guid)cmdRow["refWorkQueueCommandTypeId"] == (Guid)parameterService.GetParameterValue("WorkQueueCommandType_StateChange"))
					{
						iconName = "queue_statechange.png";
					}

					AsPanelActionButtonBuilder.Build(actionsElement, PanelActionType.QueueAction,
						PanelActionMode.MultipleCheckboxes,	ActionButtonPlacement.Toolbar, 
						cmdRow["Id"].ToString(), "", (string)cmdRow["Text"], iconName, 
						(bool)cmdRow["IsDefault"], cmdParams, confirmationMessage);
				}
			}

//			AsPanelActionButtonBuilder.Build(actionsElement, PanelActionType.Report, PanelActionMode.MultipleCheckboxes, "testReport", "Test Report", "report.png", new Hashtable());
//			AsPanelActionButtonBuilder.Build(actionsElement, PanelActionType.Workflow, PanelActionMode.MultipleCheckboxes, "testWorkflow", "Test Workflow", "", new Hashtable());
//			AsPanelActionButtonBuilder.Build(actionsElement, PanelActionType.ChangeUI, PanelActionMode.MultipleCheckboxes, "testChangeUI", "Test Change UI", "", new Hashtable());
//			AsPanelActionButtonBuilder.Build(actionsElement, PanelActionType.OpenForm, PanelActionMode.MultipleCheckboxes, "testOpenForm", "Test Open Form", "", new Hashtable());

			RenderDataSources(windowElement, dataSources);

			return doc;
		}

		internal static void AddColumn(DataStructureEntity entity, string columnName,
           ref DataStructureColumn memoColumn, ref int lastPos,
            XmlElement propertiesElement, XmlElement propertyNamesElement, DataTable table,
            string formatPattern)
        {
            AddColumn(entity, columnName, ref memoColumn,
                ref lastPos, propertiesElement, propertyNamesElement, table, formatPattern, 
                "", true, null, null);
        }

        private static void AddColumn(DataStructureEntity entity, string columnName,
	        ref DataStructureColumn memoColumn, ref int lastPos,
			XmlElement propertiesElement, XmlElement propertyNamesElement, DataTable table,
			string formatPattern, string label, bool readOnly, string lookupParameterName,
            string lookupParameterValue)
		{
			UIStyle style = null;
            SchemaService schema = ServiceManager.Services.GetService(
                typeof(SchemaService)) as SchemaService;
            StylesSchemaItemProvider styles = schema.GetProvider(
                typeof(StylesSchemaItemProvider)) as StylesSchemaItemProvider;
			int height = 0;
			DataStructureColumn col = entity.Column(columnName);
			if(col == null) 
			{
				throw new ArgumentOutOfRangeException("columnName", columnName, "Column not found in the work queue data structure.");
			}

			if(col.Field.DataType != OrigamDataType.Blob && col.Name != "Id")
			{
				if(col.Field.Name == "s1")
				{
					style = styles.GetChildByName("bold", UIStyle.CategoryConst) as UIStyle;
				}
				if(col.Field.Name == "m1")
				{
					memoColumn = col;
				}
				else if(! (col.Field.DataType == OrigamDataType.UniqueIdentifier && col.FinalLookup == null))
				{
					string caption = label == "" ? (col.Caption == "" ? col.Field.Caption : col.Caption) : label;
					height = GENERIC_FIELD_HEIGHT * (col.Field.DataType == OrigamDataType.Memo ? 6 : 1);

					XmlElement propertyElement = AsPanelPropertyBuilder.CreateProperty(
						propertiesElement: propertiesElement, 
						propertyNamesElement: propertyNamesElement, 
						modelId: col.Id, 
						bindingMember: col.Name, 
						caption: caption,
						gridCaption: null, 
						table: table,
						readOnly: readOnly, 
						left: 160, 
						top: lastPos,
						width: 600, 
						height: height,
						captionLength: 150, 
						captionPosition: "Left",
						gridColumnWidth: "100", 
						style: style,
						tabIndex: null,
						fieldType: col.Field.FieldType);

					switch(col.Field.DataType)
					{
						case OrigamDataType.Float:
						case OrigamDataType.Integer:
						case OrigamDataType.Currency:
						case OrigamDataType.Memo:
						case OrigamDataType.String:
							TextBoxBuildDefinition buildDefinition 
								= new TextBoxBuildDefinition(col.Field.DataType);
							buildDefinition.Multiline 
								= col.Field.DataType == OrigamDataType.Memo;
							TextBoxBuilder.Build(propertyElement, buildDefinition);
							break;

						case OrigamDataType.UniqueIdentifier:
							ComboBoxBuilder.Build(propertyElement, (Guid)col.FinalLookup.PrimaryKey["Id"], false, col.Name, table);
                            if (lookupParameterName != null)
                            {
                                XmlDocument doc = propertyElement.OwnerDocument;
                                XmlElement comboParametersElement = doc.CreateElement("DropDownParameters");
                                propertyElement.AppendChild(comboParametersElement);
                                XmlElement comboParamElement = doc.CreateElement("ComboBoxParameterMapping");
                                comboParametersElement.AppendChild(comboParamElement);
                                comboParamElement.SetAttribute("ParameterName", lookupParameterName);
                                comboParamElement.SetAttribute("FieldName", lookupParameterValue);
                                propertyElement.SetAttribute("Cached", XmlConvert.ToString(false));
                            }
                            break;

						case OrigamDataType.Date:
							if(formatPattern == null || formatPattern == "")
							{
								formatPattern = "dd. MM. yyyy HH:mm:ss";
							}

							DateBoxBuilder.Build(propertyElement, "Custom", formatPattern);
							break;

						case OrigamDataType.Boolean:
							CheckBoxBuilder.Build(propertyElement, caption);
							break;
					}

				}
			}

			lastPos += height + GENERIC_FIELD_VERTICAL_SPACE;
		}

        public static XmlDocument GetXml(SimpleModelData simpleModel, DataSet dataset, string name)
        {
            Hashtable dataSources = new Hashtable();
            OrigamSettings settings = ConfigurationManager.GetActiveConfiguration() as OrigamSettings;
            DataTable table = dataset.Tables["OrigamRecord"];
            SimpleModelData.OrigamEntityRow entityRow = simpleModel.OrigamEntity.Rows[0]
                as SimpleModelData.OrigamEntityRow;
            // Window
            XmlDocument doc = GetWindowBaseXml(
                name, Guid.Empty, 0, false, true, false);
            XmlElement windowElement = WindowElement(doc);
            XmlElement uiRootElement = UIRootElement(windowElement);
            XmlElement bindingsElement = ComponentBindingsElement(windowElement);

            windowElement.SetAttribute("SuppressSave", "false");
            windowElement.SetAttribute("CacheOnClient", "false");

            SplitPanelBuilder.Build(uiRootElement, SplitPanelOrientation.Horizontal, false);
            uiRootElement.SetAttribute("ModelInstanceId", "52DEFCEA-587C-47e0-97F5-3590B6AC492F");
            XmlElement children = doc.CreateElement("UIChildren");
            uiRootElement.AppendChild(children);

            XmlElement listElement = doc.CreateElement("UIElement");
            children.AppendChild(listElement);

            // Panel
            UIElementRenderData renderData = new UIElementRenderData();
            renderData.PanelTitle = entityRow.Label;
            renderData.IsGridVisible = true;
            renderData.DataMember = "OrigamRecord";
            renderData.ShowNewButton = true;
            renderData.ShowDeleteButton = true;
            if (entityRow.IsCalendarEnabled
                && ! entityRow.IsrefCalendarDateStartOrigamFieldIdNull()
                && ! entityRow.IsrefCalendarResourceOrigamFieldIdNull()
                && ! entityRow.IsrefCalendarNameOrigamFieldIdNull()
                )
            {
                renderData.IsCalendarSupported = true;
                renderData.CalendarDateFromMember = GetMember(entityRow, entityRow.refCalendarDateStartOrigamFieldId);
                if (! entityRow.IsrefCalendarDateEndOrigamFieldIdNull())
                {
                    renderData.CalendarDateToMember = GetMember(entityRow, entityRow.refCalendarDateEndOrigamFieldId);
                }
                renderData.CalendarNameMember = GetMember(entityRow, entityRow.refCalendarNameOrigamFieldId);
                if (!entityRow.IsrefCalendarResourceOrigamFieldIdNull())
                {
                    renderData.CalendarResourceIdMember = GetMember(entityRow, entityRow.refCalendarResourceOrigamFieldId);
                }
                if (! entityRow.IsrefCalendarDescriptionOrigamFieldIdNull())
                {
                    renderData.CalendarDescriptionMember = GetMember(entityRow, entityRow.refCalendarDescriptionOrigamFieldId);
                }
            }
            AsPanelBuilder.Build(listElement, renderData, entityRow.Id.ToString(), "recordPanel1", table, dataSources,
                table.PrimaryKey[0].ColumnName, false, Guid.Empty, false);

            listElement.SetAttribute("Id", "recordPanel1");
            listElement.SetAttribute("ModelInstanceId", entityRow.Id.ToString());
            listElement.SetAttribute("IsRootGrid", XmlConvert.ToString(true));
            listElement.SetAttribute("IsRootEntity", XmlConvert.ToString(true));
            listElement.SetAttribute("IsPreloaded", XmlConvert.ToString(true));
            listElement.SetAttribute("RequestDataAfterSelectionChange", XmlConvert.ToString(true));
            listElement.SetAttribute("ConfirmSelectionChange", XmlConvert.ToString(true));

            XmlElement formRootElement = AsPanelBuilder.FormRootElement(listElement);
            XmlElement propertiesElement = AsPanelBuilder.PropertiesElement(listElement);
            XmlElement actionsElement = AsPanelBuilder.ActionsElement(listElement);
            XmlElement configElement = doc.CreateElement("Configuration");
            listElement.AppendChild(configElement);

            XmlElement propertyNamesElement = doc.CreateElement("PropertyNames");
            formRootElement.AppendChild(propertyNamesElement);

            DataStructureColumn memoColumn = null;

            // Panel controls
            IPersistenceService persistence = ServiceManager.Services.GetService(
                typeof(IPersistenceService)) as IPersistenceService;
            Guid dsId = (Guid)dataset.ExtendedProperties["Id"];
            DataStructure ds = persistence.SchemaProvider.RetrieveInstance(
                typeof(DataStructure), new ModelElementKey(dsId)) as DataStructure;
            DataStructureEntity entity = ds.Entities[0] as DataStructureEntity;
            int lastPos = 5;
            if (entityRow.WorkflowCount > 0)
            {
                AddColumn(entity, "refOrigamWorkflowId", ref memoColumn,
                    ref lastPos, propertiesElement, propertyNamesElement, table, null, 
                    dataset.Tables["OrigamRecord"].Columns["refOrigamWorkflowId"].Caption,
                    false, "OrigamWorkflow_parOrigamEntityId", "'" + entityRow.Id.ToString() + "'");
                AddColumn(entity, "refOrigamStateId", ref memoColumn,
                    ref lastPos, propertiesElement, propertyNamesElement, table, null,
                    dataset.Tables["OrigamRecord"].Columns["refOrigamStateId"].Caption, false, 
                    "OrigamState_parOrigamWorkflowId", "refOrigamWorkflowId");
            }
            foreach (var column in entityRow.GetOrigamFieldRows())
            {
                AddColumn(entity, column.MappedColumn, ref memoColumn,
                    ref lastPos, propertiesElement, propertyNamesElement, table, null, column.Label, 
                    false, "OrigamRecord_parOrigamEntityId", 
                    column.IsrefLookupOrigamEntityIdNull() ? null : "'" + column.refLookupOrigamEntityId.ToString() + "'");
            }
            ArrayList validActions = new ArrayList();
            UIActionTools.GetValidActions(
                formId: entityRow.Id,
                panelId: entityRow.Id,
                disableActionButtons: renderData.DisableActionButtons, 
                entityId: table.ExtendedProperties.Contains("EntityId") 
                    ? (Guid)table.ExtendedProperties["EntityId"] : Guid.Empty, 
                validActions: validActions);
            UserProfile profile = SecurityManager.CurrentUserProfile();
            EntityUIAction actionToRemove = null;
            foreach (EntityUIAction action in validActions)
            {
                if (action.Name == "Design" && !entityRow.RecordCreatedBy.Equals(profile.Id))
                {
                    actionToRemove = action;
                    break;
                }
            }
            if (actionToRemove != null)
            {
                validActions.Remove(actionToRemove);
            }
            Hashtable designButtonParams = new Hashtable();
            designButtonParams.Add("OrigamEntity_parId", "'" + entityRow.Id.ToString() + "'");
            IParameterService parameterService = ServiceManager.Services.GetService(typeof(IParameterService)) as IParameterService;
            RenderActions(parameterService, validActions, actionsElement,
                designButtonParams);
            SetUserConfig(doc, listElement, null, entityRow.Id, Guid.Empty);
            RenderDataSources(windowElement, dataSources);
            return doc;
        }

        private static string GetMember(SimpleModelData.OrigamEntityRow entityRow, Guid memberId)
        {
            foreach (var item in entityRow.GetOrigamFieldRows())
            {
                if (item.Id.Equals(memberId))
                {
                    return item.MappedColumn;
                }
            }
            throw new ArgumentOutOfRangeException("memberId", memberId, "Member not found");
        }

        public static XmlDocument GetXml(string dashboardViewConfig, string name, Guid menuId, XmlDocument dashboardViews)
		{
			XmlDocument configXml = new XmlDocument();
			configXml.LoadXml(dashboardViewConfig);

			DashboardConfiguration config = DashboardConfiguration.Deserialize(configXml);

			XmlDocument doc = GetWindowBaseXml(name, menuId, 0, false, false, false);
			XmlElement windowElement = WindowElement(doc);
			XmlElement uiRootElement = UIRootElement(windowElement);
			XmlElement bindingsElement = ComponentBindingsElement(windowElement);
			XmlElement dataSourcesElement = DataSourcesElement(windowElement);

			XmlElement views = doc.CreateElement("DashboardViews");
			windowElement.AppendChild(views);

			if(dashboardViews != null)
			{
				views.InnerXml = dashboardViews.FirstChild.InnerXml;
			}

			GridLayoutPanelBuilder.Build(uiRootElement);
			XmlElement children = doc.CreateElement("UIChildren");
			uiRootElement.AppendChild(children);

            if (config.Items != null)
            {
                foreach (DashboardConfigurationItem item in config.Items)
                {
                    DashboardConfigurationItemBuilder.Build(doc, children, menuId, item, dataSourcesElement);
                }

                // component bindings (after all grids are configured)
                foreach (DashboardConfigurationItem item in config.Items)
                {
                    if (item.Parameters != null)
                    {
                        foreach (DashboardConfigurationItemParameter param in item.Parameters)
                        {
                            if (param.IsBound)
                            {
                                string parentId = param.BoundItemId.ToString();
                                string parentProperty = param.BoundItemProperty;
                                string parentEntity = EntityByGridInstanceId(doc, parentId);
                                string childEntity = EntityByGridInstanceId(doc, item.Id.ToString());

                                CreateComponentBinding(doc, bindingsElement, parentId, parentProperty, parentEntity, item.Id.ToString(), param.Name, childEntity, true);
                            }
                        }
                    }
                }
            }

			return doc;
		}

		public static XmlOutput GetXml(FormControlSet item, DataSet dataset, 
			string name, bool isPreloaded, Guid menuId, bool forceReadOnly, 
			string confirmSelectionChangeEntity, DataStructure structure=null)
		{
			int controlCounter = 0;
			IPersistenceService ps = ServiceManager.Services.GetService(typeof(IPersistenceService)) as IPersistenceService;
			WorkflowReferenceMenuItem wfmi = ps.SchemaProvider.RetrieveInstance(typeof(WorkflowReferenceMenuItem), new ModelElementKey(menuId)) as WorkflowReferenceMenuItem;
			FormReferenceMenuItem frmi = ps.SchemaProvider.RetrieveInstance(typeof(FormReferenceMenuItem), new ModelElementKey(menuId)) as FormReferenceMenuItem;
			Guid workflowId = (wfmi == null ? Guid.Empty : wfmi.WorkflowId);
			int autoRefreshInterval = 0;
			Hashtable dataSources = new Hashtable();
			if(frmi != null && frmi.AutoRefreshInterval != null)
			{
				IParameterService parameterService = ServiceManager.Services.GetService(typeof(IParameterService)) as IParameterService;
				autoRefreshInterval = (int)parameterService.GetParameterValue(frmi.AutoRefreshIntervalConstantId, OrigamDataType.Integer);
			}
			// Window
			XmlDocument doc = GetWindowBaseXml(
				name, menuId, autoRefreshInterval, 
				(frmi != null) ? frmi.RefreshOnFocus : false,
                (frmi != null) ? frmi.AutoSaveOnListRecordChange : false,
                (frmi != null) ? frmi.RequestSaveAfterUpdate : false);
			XmlElement windowElement = WindowElement(doc);
			XmlElement uiRootElement = UIRootElement(windowElement);
			if(forceReadOnly)
			{
				windowElement.SetAttribute("SuppressSave", "true");
			}

			if (frmi?.ListDataStructure != null)
			{
				windowElement.SetAttribute("UseSession", "true");
			}

			XmlOutput xmlOutput = new XmlOutput {Document = doc};

		    RenderUIElement(xmlOutput, uiRootElement, FormTools.GetItemFromControlSet(item),
				dataset, dataSources, ref controlCounter, isPreloaded, item.Id, workflowId, 
				forceReadOnly, confirmSelectionChangeEntity, structure);
			RenderDataSources(windowElement, dataSources);
			// check if there is no root grid
			XmlNodeList grids = doc.SelectNodes("//*[(@Type='Grid' or @Type='TreePanel' or @Type='ReportButton') and @IsRootGrid = 'true']");
			if(grids.Count == 0)
			{
				grids = doc.SelectNodes("//*[(@Type='Grid' or @Type='TreePanel' or @Type='ReportButton') and @IsRootEntity = 'true']");
				if(grids.Count > 0)
				{
					(grids[0] as XmlElement).SetAttribute("IsRootGrid", "true");
				}
			}
			grids = doc.SelectNodes("//*[@Type='Grid' or @Type='TreePanel' or @Type='ReportButton' or @Type='SectionLevelPlugin']");
			foreach(XmlElement g in grids)
			{
				if(g.GetAttribute("IsRootGrid") == "false" && g.GetAttribute("IsRootEntity") == "false")
				{
					// if this panel has no navigation and displays form (detail) view, so we think it displays
					// only a single record so we try to find a panel on the same entity that has navigation
					// and connect them 1=1.
					if((g.GetAttribute("IsHeadless") == "true" || g.GetAttribute("DisableActionButtons") == "true")
						&& 
                        (g.GetAttribute("DefaultPanelView") == XmlConvert.ToString((int)OrigamPanelViewMode.Form)
                            || g.GetAttribute("DefaultPanelView") == XmlConvert.ToString((int)OrigamPanelViewMode.Map)
                        )
                       )
					{
						bool found = false;
						// try finding parent grid in the same entity - with head
						foreach(XmlElement parentGrid in grids)
						{
							if((parentGrid.GetAttribute("Entity") == g.GetAttribute("Entity")) 
                            && (
                            (parentGrid.GetAttribute("Type") == "TreePanel")
                            ||
								((parentGrid.GetAttribute("IsHeadless") == "false")
								&& (parentGrid.GetAttribute("DisableActionButtons") == "false")))
								)
							{
								g.SetAttribute("ParentId", parentGrid.GetAttribute("ModelInstanceId"));
								g.SetAttribute("ParentEntityName", parentGrid.GetAttribute("Entity"));
								g.SetAttribute("IsPreloaded", XmlConvert.ToString(true));
								found = true;
								break;
							}
						}

						if(! found)
						{
							// there is no grid in the same entity, we find one in the parent entity
							XmlElement parentGrid = FindParentGridInParentEntity(dataset, g.GetAttribute("DataMember"), grids);

							if(parentGrid != null)
							{
								g.SetAttribute("ParentId", parentGrid.GetAttribute("ModelInstanceId"));
								g.SetAttribute("ParentEntityName", parentGrid.GetAttribute("Entity"));
							}
						}
					}
					else
					{
						XmlElement parentGrid = FindParentGridInParentEntity(dataset, g.GetAttribute("DataMember"), grids);

						if(parentGrid != null)
						{
							g.SetAttribute("ParentId", parentGrid.GetAttribute("ModelInstanceId"));
							g.SetAttribute("ParentEntityName", parentGrid.GetAttribute("Entity"));
						}
					}
				}
				else if(g.GetAttribute("IsRootGrid") == "false" & g.GetAttribute("IsRootEntity") == "true")
				{
					// we lookup a root grid
					foreach(XmlElement parentGrid in grids)
					{
						if(parentGrid.GetAttribute("IsRootGrid") == "true" && parentGrid.GetAttribute("Entity") == g.GetAttribute("Entity"))
						{
							g.SetAttribute("ParentId", parentGrid.GetAttribute("ModelInstanceId"));
							g.SetAttribute("ParentEntityName", parentGrid.GetAttribute("Entity"));
							break;
						}
					}
				}

				if(g.GetAttribute("IsRootGrid") == "false" && (g.GetAttribute("ParentId") == null || g.GetAttribute("ParentId") == ""))
				{
					g.SetAttribute("IsRootGrid", "true");
				}
			}

			XmlElement bindingsElement = ComponentBindingsElement(windowElement);

			// set filters between related grids & lazy loading parameters
			foreach(XmlElement g in grids)
			{
				// set lazy loading parameters
				if(g.GetAttribute("Entity") == confirmSelectionChangeEntity && g.GetAttribute("IsRootGrid") == "true")
				{
					g.SetAttribute("RequestDataAfterSelectionChange", XmlConvert.ToString(true));
					g.SetAttribute("ConfirmSelectionChange", XmlConvert.ToString(true));
				}

				// the grid references some parent grid
				if(g.GetAttribute("ParentId").Length > 0)
				{
					string entity = g.GetAttribute("Entity");
					string parentEntity = g.GetAttribute("ParentEntityName");
					string dataMember = g.GetAttribute("DataMember");
					string parentId = g.GetAttribute("ParentId");

					if(dataMember == "") dataMember = entity;

					string parentDataMember = DataMemberByGridInstanceId(doc, parentId);

					DataTable t = dataset.Tables[entity];

					if(entity == parentEntity && dataMember == parentDataMember)	// references to the same entity
					{
						XmlElement filterExpressionsElement = doc.CreateElement("FilterExpressions");
						g.AppendChild(filterExpressionsElement);

						foreach(DataColumn col in t.PrimaryKey)
						{
							CreateComponentBinding(doc, bindingsElement, parentId, col.ColumnName, parentEntity, g.GetAttribute("ModelInstanceId"), col.ColumnName, entity, false);
						}
					}
					else						// references to the parent entity
					{
						DataRelation relation = null;
						
						foreach(DataRelation r in t.ParentRelations)
						{
							if(r.ParentTable.TableName == parentEntity)
							{
								relation = r;
								break;
							}
						}

						if(relation == null) throw new ArgumentOutOfRangeException("ParentEntityName", parentEntity, "Parent entity not found for entity '" + entity + "'. Cannot generate filters.");

						XmlElement filterExpressionsElement = doc.CreateElement("FilterExpressions");
						g.AppendChild(filterExpressionsElement);

						for(int i = 0; i < relation.ChildColumns.Length; i++)
						{
							CreateComponentBinding(doc, bindingsElement, parentId, relation.ParentColumns[i].ColumnName, parentEntity, g.GetAttribute("ModelInstanceId"), relation.ChildColumns[i].ColumnName, entity, false);
						}
					}
				}
			}

			return xmlOutput;
		}

		private static XmlElement FindComponentByInstanceId(XmlDocument doc, string instanceId)
		{
			XmlElement result = (XmlElement)doc.SelectSingleNode("//*[@ModelInstanceId='" + instanceId + "']");

			if (result == null)
			{
				throw new ArgumentOutOfRangeException("instanceId", instanceId, "Component with specified model instance id not found.");
			}

			return result;
		}

		private static string DataMemberByGridInstanceId(XmlDocument doc, string instanceId)
		{
			XmlElement parentGrid = FindComponentByInstanceId(doc, instanceId);

			return parentGrid.GetAttribute("DataMember");
		}

		private static string EntityByGridInstanceId(XmlDocument doc, string instanceId)
		{
			XmlElement parentGrid = FindComponentByInstanceId(doc, instanceId);

			return parentGrid.GetAttribute("Entity");
		}

		private static void CreateComponentBinding(XmlDocument doc, XmlElement bindingsElement, 
			string parentId, string parentProperty, string parentEntity, 
			string childId, string childProperty, string childEntity, bool isChildParameter)
		{
			XmlElement binding = doc.CreateElement("Binding");
			binding.SetAttribute("ParentId", parentId);
			binding.SetAttribute("ParentProperty", parentProperty);

			if(parentEntity != null)
			{
				binding.SetAttribute("ParentEntity", parentEntity);
			}

			binding.SetAttribute("ChildId", childId);
			binding.SetAttribute("ChildProperty", childProperty);

			if(childEntity != null)
			{
				binding.SetAttribute("ChildEntity", childEntity);
			}

			if(isChildParameter)
			{
				binding.SetAttribute("ChildPropertyType", "Parameter");
			}
			else
			{
				binding.SetAttribute("ChildPropertyType", "Field");
			}

			bindingsElement.AppendChild(binding);
		}

		private static XmlElement FindParentGridInParentEntity(DataSet dataset, string dataMember, XmlNodeList grids)
		{
//			DataTable gridTable = dataset.Tables[entity];
//			if(gridTable.ParentRelations.Count > 0)
//			{
//				// find parent grid
//				foreach(XmlElement parentGrid in grids)
//				{
//					if(parentGrid.GetAttribute("Type") != "ReportButton" && parentGrid.GetAttribute("Entity") == gridTable.ParentRelations[0].ParentTable.TableName)
//					{
//						return parentGrid;
//					}
//				}
//			}

			int lastDot = dataMember.LastIndexOf(".");

			if(lastDot > 0)
			{
				string parentMember = dataMember.Substring(0, lastDot);

				// find parent grid
				foreach(XmlElement parentGrid in grids)
				{
					if(parentGrid.GetAttribute("Type") != "ReportButton" && parentGrid.GetAttribute("DataMember").ToLower() == parentMember.ToLower())
					{
						return parentGrid;
					}
				}
			}

			return null;
		}

		private static bool RenderUIElement(XmlOutput xmlOutput, XmlElement parentNode, AbstractSchemaItem item, 
			DataSet dataset, Hashtable dataSources, ref int controlCounter, bool isPreloaded, Guid formId, Guid menuWorkflowId,
			bool forceReadOnly, string confirmSelectionChangeEntity, DataStructure structure=null)
		{
			ControlSetItem control = item as ControlSetItem;
			IParameterService parameterService = ServiceManager.Services.GetService (typeof(IParameterService)) as IParameterService;

			if (control.ControlItem.Name == "AsForm") {
				// for the Form we find its root control and we continue
				if(control.ChildItemsByType(ControlSetItem.CategoryConst).Count==0) 
				{ 
					return false; 
				}
				item = (AbstractSchemaItem)control.ChildItemsByType (ControlSetItem.CategoryConst) [0];
				control = item as ControlSetItem;
			}

			if (!RenderTools.ShouldRender(control)) {
				return false;
			}

			forceReadOnly = FormTools.GetReadOnlyStatus (control, forceReadOnly);

			UIElementRenderData renderData = UIElementRenderData.GetRenderData (control, forceReadOnly);
            if (renderData.Style != null)
            {
                parentNode.SetAttribute("Style", renderData.Style.StyleDefinition());
            }
            
            DataTable table;
			
			if (renderData.IndependentDataSourceId == Guid.Empty) {
				table = dataset.Tables [FormTools.FindTableByDataMember (dataset, renderData.DataMember)];
			} else {
				IPersistenceService ps = ServiceManager.Services.GetService (typeof(IPersistenceService)) as IPersistenceService;
				DataStructure ds = (DataStructure)ps.SchemaProvider.RetrieveInstance (typeof(DataStructure), 
					                   new ModelElementKey (renderData.IndependentDataSourceId));

				DatasetGenerator dg = new DatasetGenerator (true);
				DataSet independentDataset = dg.CreateDataSet (ds);
				table = independentDataset.Tables [0];
			}

			parentNode.SetAttribute ("Id", control.Name + "_" + controlCounter.ToString ());
			controlCounter++;
			parentNode.SetAttribute ("ModelInstanceId", control.Id.ToString ());
//			CreateAttribute(doc, parentNode, "X", XmlConvert.ToString(left));
//			CreateAttribute(doc, parentNode, "Y", XmlConvert.ToString(top));

			// for the first control in the fixed sized splitter we must set the height of the control
			// the second control in the splitter will have 100% size
			XmlElement parentControlElement = parentNode.ParentNode.ParentNode as XmlElement;
			if (renderData.TabIndex == 0 && parentControlElement != null
			   && (parentControlElement.GetAttribute ("Type") == "VBox" || parentControlElement.GetAttribute ("Type") == "CollapsiblePanel")) {
				parentNode.SetAttribute ("Height", XmlConvert.ToString (renderData.Height));
			}
			
			if (renderData.TabIndex == 0 && parentControlElement != null && parentControlElement.GetAttribute ("Type") == "HBox") {
				parentNode.SetAttribute ("Width", XmlConvert.ToString (renderData.Width));
			}

			bool isIndependent = renderData.IndependentDataSourceId != Guid.Empty;
			if (!control.ControlItem.IsComplexType)
			{
				var controlItem = GentControlItem(control);
				switch (controlItem.Name)
                {
                    case "Panel":
                        PanelBuilder.Build(parentNode);
                        break;
                    case "AsReportPanel":
                        ReportPanelBuilder.Build(parentNode, renderData, table, control);
                        break;
                    case "TabControl":
                        TabControlBuilder.Build(parentNode);
                        break;
                    case "TabPage":
                        TabBuilder.Build(parentNode, renderData.Text);
                        break;
                    case "CollapsibleContainer":
                        CollapsibleContainerBuilder.Build(parentNode);
                        break;
                    case "CollapsiblePanel":
                        CollapsiblePanelBuilder.Build(parentNode, renderData);
                        break;
                    case "GridLayoutPanel":
                        GridLayoutPanelBuilder.Build(parentNode);
                        break;
                    case "GridLayoutPanelItem":
                        GridLayoutPanelItemBuilder.Build(parentNode, renderData);
                        break;
                    case "SplitPanel":
                        SplitPanelBuilder.Build(parentNode,
                            (SplitPanelOrientation)renderData.Orientation, renderData.FixedSize);
                        break;
                    case "AsTree":
                        TreeControlBuilder.Build(parentNode, renderData, table,
                            control.Id.ToString(), dataSources, false);
                        break;
                    case "AsTree2":
                        TreeControlBuilder.Build2(parentNode, renderData.FormParameterName,
                            renderData.TreeId);
                        break;
                    case "ScreenLevelPlugin":
	                    ScreenLevelPluginBuilder.Build(
		                    parentNode: parentNode, 
		                    text: renderData.Text,
		                    dataSources: dataSources, 
		                    dataset: dataset,
		                    dataStructure: structure,
		                    dataMember: renderData.DataMember
		                    );
	                    break;                    
                    case "SectionLevelPlugin":
	                    SectionLevelPluginBuilder.Build(
		                    parentNode: parentNode, 
		                    text: renderData.Text,
		                    table: table, 
		                    dataStructure: structure, 
		                    isPreloaded: isPreloaded,
		                    isIndependent: isIndependent, 
		                    dataSources: dataSources, 
		                    modelId: control.Id.ToString(),
		                    dataMember: renderData.DataMember);
	                    break;
                    case "Label":
                        FormLabelBuilder.Build(parentNode, renderData.Text);
                        break;
                    default:
                        parentNode.SetAttribute("type", "http://www.w3.org/2001/XMLSchema-instance", "UIElement");
                        parentNode.SetAttribute("Type", "Box");
                        parentNode.SetAttribute("Title", "UNKNOWN CONTROL:" + control.Name);
                        break;
                }

                AddDynamicProperties(parentNode, renderData);
            }
            else // complex type = screen section
            {
	            if (table == null)
	            {
		            if (string.IsNullOrWhiteSpace(renderData.DataMember))
		            {
			            throw new NullReferenceException("DataMember not set for a screen section inside a screen. Cannot render a screen section. " + control.Path);
		            }
		            throw new Exception($"DataMember {renderData.DataMember} was not found in data source. Cannot render a screen section. {control.Path}");
	            }
	            if (table.PrimaryKey.Length == 0)
                    throw new Exception("Panel's data source has no primary key. Cannot render panel. " + control.Path);
                // get list of valid actions and set the panel multi-select-checkbox column visibility
                ArrayList validActions = new ArrayList();
                bool hasMultipleSelection = UIActionTools.GetValidActions(
                    formId, control.ControlItem.PanelControlSet.Id, 
                    renderData.DisableActionButtons, 
                    table.ExtendedProperties.Contains("EntityId") 
                        ? (Guid)table.ExtendedProperties["EntityId"] : Guid.Empty, 
                    validActions);
                AsPanelBuilder.Build(parentNode, renderData,
                    FormTools.GetItemFromControlSet(control.ControlItem.PanelControlSet).Id.ToString(),
                    control.Id.ToString(), table, dataSources,
                    table.PrimaryKey[0].ColumnName, hasMultipleSelection, formId,
                    isIndependent);
                XmlElement formRootElement = AsPanelBuilder.FormRootElement(parentNode);
                XmlElement propertiesElement = AsPanelBuilder.PropertiesElement(parentNode);
                XmlElement actionsElement = AsPanelBuilder.ActionsElement(parentNode);
                RenderActions(parameterService, validActions, actionsElement,
                    new Hashtable());
                // render controls (both directly placed edit controls and containers)
                RenderPanel(control, xmlOutput, table, formRootElement, propertiesElement,
                    FormTools.GetItemFromControlSet(control.ControlItem.PanelControlSet), true, true, forceReadOnly);
            }

            // add config
            SetUserConfig (xmlOutput.Document, parentNode, renderData.DefaultConfiguration, control.Id, menuWorkflowId);

			ArrayList sortedChildren = new ArrayList (item.ChildItemsByType (ControlSetItem.CategoryConst));

			if (sortedChildren.Count > 0) {
				sortedChildren.Sort (new ControlSetItemComparer ());

				XmlElement children = xmlOutput.Document.CreateElement ("UIChildren");
				parentNode.AppendChild (children);
				foreach (AbstractSchemaItem child in sortedChildren) {
					XmlElement el = xmlOutput.Document.CreateElement ("UIElement");
					children.AppendChild (el);
					if (!RenderUIElement (xmlOutput, el, child, dataset, dataSources, 
						ref controlCounter, isPreloaded, formId, menuWorkflowId,
						forceReadOnly, confirmSelectionChangeEntity, structure)) {
						children.RemoveChild (el);
					}
				}
			}

			// child grid filter expressions
			bool hasParentTables = false;

			if (table != null && renderData.DataMember != null) {
				hasParentTables = table.ParentRelations.Count > 0 & renderData.DataMember.IndexOf (".") >= 0;
			}

			if (isIndependent) {
				parentNode.SetAttribute ("IsRootGrid", XmlConvert.ToString (true));
				parentNode.SetAttribute ("IsRootEntity", XmlConvert.ToString (true));
				parentNode.SetAttribute ("IsPreloaded", XmlConvert.ToString (false));
			} else if (control.ControlItem.Name == "AsReportPanel") {
				parentNode.SetAttribute ("IsRootGrid", XmlConvert.ToString (false));
				parentNode.SetAttribute ("IsRootEntity", XmlConvert.ToString (!hasParentTables));
			}
			// grid or tree
			else if (hasParentTables) {
				DataRelation relation = table.ParentRelations [0];

				parentNode.SetAttribute ("IsRootGrid", XmlConvert.ToString (false));
				parentNode.SetAttribute ("IsRootEntity", XmlConvert.ToString (false));

				if (renderData.HideNavigationPanel) {
					parentNode.SetAttribute ("IsPreloaded", XmlConvert.ToString (true));
				} else {
					parentNode.SetAttribute ("IsPreloaded", XmlConvert.ToString (isPreloaded));
				}
			}
			else if (control.ControlItem.Name == "SectionLevelPlugin")
			{
				if (renderData.AllowNavigation)
				{
					parentNode.SetAttribute("IsRootGrid", XmlConvert.ToString(true));
					parentNode.SetAttribute("IsRootEntity", XmlConvert.ToString(true));
					parentNode.SetAttribute("IsPreloaded", XmlConvert.ToString(false));
				}
				else
				{
					parentNode.SetAttribute ("IsRootGrid", XmlConvert.ToString (false));
					parentNode.SetAttribute ("IsRootEntity", XmlConvert.ToString (true));
					parentNode.SetAttribute ("IsPreloaded", XmlConvert.ToString (true));
				}
			}
			// grid without navigation on a root level but without an implicit filter
			// if there is an implicit filter it must be an independent panel
			else if ((renderData.ImplicitFilter == null || renderData.ImplicitFilter == "" &&
			         (renderData.HideNavigationPanel || (renderData.ShowDeleteButton == false && renderData.ShowNewButton == false)))) {
				parentNode.SetAttribute ("IsRootGrid", XmlConvert.ToString (false));
				parentNode.SetAttribute ("IsRootEntity", XmlConvert.ToString (true));
				parentNode.SetAttribute ("IsPreloaded", XmlConvert.ToString (true));
			}
			// root grid with navigation or SectionLevelPlugin
			else {
				parentNode.SetAttribute ("IsRootGrid", XmlConvert.ToString (true));
				parentNode.SetAttribute ("IsRootEntity", XmlConvert.ToString (true));
				parentNode.SetAttribute ("IsPreloaded", XmlConvert.ToString (true));
			}

			return true;
		}

		private static AbstractSchemaItem GentControlItem(ControlSetItem control)
		{
			if (control.ControlItem.Ancestors.Count > 1)
			{
				throw new Exception(
					$"Could not find control for {control.ControlItem.Name} because it has more than one ancestor.");
			}

			return control.ControlItem.Ancestors.Count == 1
				? control.ControlItem.Ancestors[0].Ancestor
				: control.ControlItem;
		}

		private static void AddDynamicProperties(XmlElement parentNode, UIElementRenderData renderData)
		{
			foreach (var pair in renderData.DynamicProperties)
			{
				parentNode.SetAttribute(pair.Key, pair.Value);
			}
		}

		private static void RenderActions(IParameterService parameterService, 
            ArrayList validActions, XmlElement actionsElement, Hashtable inputParameters)
        {
            // render action buttons
            foreach (EntityUIAction action in validActions)
            {
                Hashtable parameters = new Hashtable(inputParameters);
                foreach (EntityUIActionParameterMapping mapping
                    in action.ChildItemsByType(
                    EntityUIActionParameterMapping.CategoryConst))
                {
                    parameters.Add(mapping.Name, mapping.Field);
                }
                string groupId = "";
                if (action.ParentItem is EntityDropdownAction)
                {
                    groupId = action.ParentItemId.ToString();
                }
                bool shShift = false;
                bool shAlt = false;
                bool shCtrl = false;
                int shKey = 0;
                KeyboardShortcut sh = action.KeyboardShortcut;
                int terminator = 0;
                if (sh != null)
                {
                    shShift = sh.IsShift;
                    shAlt = sh.IsAlt;
                    shCtrl = sh.IsControl;
                    shKey = sh.KeyCode;
                }
                if ((action.ScannerTerminator != "")
                && (action.ScannerTerminator != null))
                {
                    terminator = int.Parse(action.ScannerTerminator);
                }
                string confirmationMessage = null;
                if (action.ConfirmationMessage != null)
                {
                    confirmationMessage = parameterService.GetString(
                        action.ConfirmationMessage.Name);
                }
                AsPanelActionButtonBuilder.Build(actionsElement,
                    action.ActionType, action.Mode, action.Placement,
                    action.Id.ToString(), groupId, action.Caption,
                    action.ButtonIcon == null ? "" : action.ButtonIcon.Name,
                    action.IsDefault, parameters,
                    shShift, shCtrl, shAlt, shKey,
                    action.ScannerInputParameter, terminator,
                    confirmationMessage);
            }
        }

		private static void SetUserConfig(XmlDocument doc, XmlNode parentNode, string defaultConfiguration, Guid objectId, Guid workflowId)
		{
            UserProfile profile = SecurityManager.CurrentUserProfile();
			DataSet userConfig = OrigamPanelConfigDA.LoadConfigData(objectId, workflowId, profile.Id);
			
			if (userConfig.Tables["OrigamFormPanelConfig"].Rows.Count == 0)
			{
				if(defaultConfiguration != null && defaultConfiguration != "")
				{
					XmlDocumentFragment configElement = doc.CreateDocumentFragment();
					configElement.InnerXml = defaultConfiguration;
					parentNode.AppendChild(configElement);
				}
				else
				{
					XmlElement configElement = doc.CreateElement("Configuration");
					parentNode.AppendChild(configElement);
				}
			}
			else
			{
				XmlDocumentFragment configElement = doc.CreateDocumentFragment();

				object data = userConfig.Tables["OrigamFormPanelConfig"].Rows[0]["SettingsData"];

				if(data is String)
				{
					configElement.InnerXml = (string)data;
				}

				parentNode.AppendChild(configElement);
			}
		}

		private static void RenderPanel(ControlSetItem panel, XmlOutput xmlOutput, DataTable table,
			XmlElement parentElement, XmlElement propertiesElement, AbstractSchemaItem item,
			bool processContainers, bool processEditControls, bool forceReadOnly, string itemTabIndex = null)
		{

			if (string.IsNullOrWhiteSpace(itemTabIndex))
			{
				int parentTabIndex = item.ChildItemsByType(PropertyValueItem.CategoryConst)
					.Cast<PropertyValueItem>()
					.FirstOrDefault(prop => prop.ControlPropertyItem.Name == "TabIndex")?.IntValue ?? -1;
				itemTabIndex = parentTabIndex.ToString();
			}

			XmlElement childrenElement = xmlOutput.Document.CreateElement("Children");
			parentElement.AppendChild(childrenElement);

			XmlElement propertyNamesElement = xmlOutput.Document.CreateElement("PropertyNames");
			parentElement.AppendChild(propertyNamesElement);

			XmlElement formExclusiveControlsElement = xmlOutput.Document.CreateElement("FormExclusiveControls");
			parentElement.AppendChild(formExclusiveControlsElement);

			// other properties
			ArrayList childItems = new ArrayList(item.ChildItemsByType(ControlSetItem.CategoryConst));
			childItems.Sort(new ControlSetItemComparer());

			foreach(ControlSetItem csi in childItems)
			{
				if(RenderTools.ShouldRender(csi))
				{
					string caption = "";
					string gridCaption = "";
					string bindingMember = "";
					string tabIndex = "0";
					Guid lookupId = Guid.Empty;
                    bool readOnly = forceReadOnly;
                    if (!forceReadOnly)
                    {
                        FormTools.GetReadOnlyStatus(csi, false);
                    }
					string text = "";
					bool showUniqueValues = false;
					int top = 0;
					int left = 0;
					int width = 0;
					int height = 0;
					int captionLength = 100;
					string captionPosition = "Left";
					string dock = "None";
					bool multiline = false;
					bool isPassword = false;
					string gridColumnWidth = null;
					bool isRichText = false;
					bool allowTab = false;
					bool hideOnForm = false;
					Guid styleId = Guid.Empty;
					UIStyle style = null;
					string format = null;
					string customFormat = null;
					string sourceType = null;
					int columnWidth = 100;
					Guid dataConstantId = Guid.Empty;
					string controlMember = "";
                    string customNumericFormat = "";

					foreach(PropertyValueItem property in csi.ChildItemsByType(PropertyValueItem.CategoryConst))
					{
						string stringValue = property.Value;
						if(stringValue != null && DatasetGenerator.IsCaptionExpression(stringValue))
						{
							stringValue = DatasetGenerator.EvaluateCaptionExpression(stringValue);
						}

						switch(property.ControlPropertyItem.Name)
						{
							case "TabIndex":			tabIndex = property.IntValue.ToString();   							    break;
							case "Text":				text = stringValue;														break;
							case "Caption":				caption = stringValue;													break;
							case "GridColumnCaption":	gridCaption = stringValue;												break;
							case "LookupId":			lookupId = property.GuidValue;											break;
							case "ReadOnly":			readOnly = (readOnly ? true : property.BoolValue);  					break;
							case "ShowUniqueValues":	showUniqueValues = property.BoolValue;									break;
							case "Top":					top = property.IntValue;												break;
							case "Left":				left = property.IntValue;												break;
							case "Width":				width = property.IntValue;												break;
							case "Height":				height = property.IntValue;												break;
							case "CaptionLength":		captionLength = property.IntValue;										break;
							case "CaptionPosition":		captionPosition = ((CaptionPosition)property.IntValue).ToString();		break;
							case "SourceType":			sourceType = ((ImageBoxSourceType)property.IntValue).ToString();		break;
							case "Dock":				dock = ToWinFormsDockStyle(property.IntValue);	                        break;
							case "Multiline":			multiline = property.BoolValue;											break;
							case "IsPassword":			isPassword = property.BoolValue;										break;
							case "GridColumnWidth":		gridColumnWidth = property.IntValue.ToString();							break;
							case "IsRichText":			isRichText = property.BoolValue;										break;
							case "AllowTab":			allowTab = property.BoolValue;											break;
							case "HideOnForm":			hideOnForm = property.BoolValue;										break;
							case "StyleId":				styleId = property.GuidValue;											break;
							case "Format":
								if(property.Value == null)
								{
									format = "Long";
								}
								else
								{
									XmlDocument formatDoc = new XmlDocument();
									formatDoc.LoadXml(property.Value);
									format = formatDoc.SelectSingleNode("DateTimePickerFormat").InnerText;
								}
								break;
							case "CustomFormat":		customFormat = stringValue;												break;
							case "ColumnWidth":			columnWidth = property.IntValue;										break;
							case "DataConstantId":		dataConstantId = property.GuidValue;									break;
							case "ControlMember":		controlMember = property.Value;											break;
                            case "CustomNumericFormat": 
                                customNumericFormat = property.Value;
                                break;
						}
					}

					if(! styleId.Equals(Guid.Empty))
					{
						IPersistenceService persistence = ServiceManager.Services.GetService(
                            typeof(IPersistenceService)) as IPersistenceService;
						style = persistence.SchemaProvider.RetrieveInstance(
                            typeof(UIStyle), new ModelElementKey(styleId)) as UIStyle;
					}

					ControlSetItem parentPanel = csi.ParentItem as ControlSetItem;
					if(parentPanel != null && parentPanel.ControlItem.Name == "AsPanel")
					{
						PropertyValueItem hideProperty = parentPanel.GetChildByName(
                            "HideNavigationPanel", PropertyValueItem.CategoryConst) as PropertyValueItem;
						if(hideProperty != null)
						{
							if(!hideProperty.BoolValue)
							{
								top -= 20;
							}
						}
					}

					foreach(PropertyBindingInfo bindItem in csi.ChildItemsByType(PropertyBindingInfo.CategoryConst))
					{
						bindingMember = bindItem.Value;
					}
					var fieldType = csi.FirstParentOfType<PanelControlSet>()
						?.DataEntity
						?.ChildItems?.ToGeneric()
						?.OfType<IDataEntityColumn>()
						?.FirstOrDefault(child => child.Name == bindingMember)
						?.FieldType ?? "";
					
					if (int.Parse(tabIndex) >= 0)
					{
						tabIndex = itemTabIndex + "." + tabIndex;
					}

					if(csi.ControlItem.Name == "Label" && processContainers)
					{
						PanelLabelBuilder.Build(childrenElement, text, top, left, height, width);
					}
					else if(csi.ControlItem.Name == "RadioButton" && processEditControls)
					{
						if(! table.Columns.Contains(bindingMember)) throw new Exception("Field '" + bindingMember + "' not found in a data structure for the form '" + panel.RootItem.Path + "'");
						
						XmlElement controlElement = AsPanelPropertyBuilder.CreateProperty(
							category: "Control", 
							propertiesElement: formExclusiveControlsElement,
							propertyNamesElement: null, 
							modelId: csi.Id,
							bindingMember: bindingMember,
							caption: caption, 
							gridCaption: gridCaption, 
							table: table,
							readOnly: readOnly,
							left: left, 
							top: top, 
							width: width, 
							height: height, 
							captionLength: captionLength,
							captionPosition: captionPosition, 
							gridColumnWidth: gridColumnWidth == "0" ? "" : gridColumnWidth, 
							style: style,
							tabIndex: tabIndex,
							fieldType: fieldType);

						IParameterService parameterService = ServiceManager.Services.GetService(typeof(IParameterService)) as IParameterService;
						string value = (string)parameterService.GetParameterValue(dataConstantId, OrigamDataType.String);

						RadioButtonBuilder.Build(controlElement, text, value);
					}
					else if(bindingMember == "" && processContainers)	// visual element
					{
						XmlElement formElement = xmlOutput.Document.CreateElement("FormElement");
						childrenElement.AppendChild(formElement);

						formElement.SetAttribute("Type", "FormSection");
						formElement.SetAttribute("Title", text);
						formElement.SetAttribute("TabIndex", tabIndex);

						formElement.SetAttribute("X", XmlConvert.ToString(left));
						formElement.SetAttribute("Y", XmlConvert.ToString(top));
						formElement.SetAttribute("Width", XmlConvert.ToString(width));
						formElement.SetAttribute("Height", XmlConvert.ToString(height));

						RenderPanel(
							panel: panel, 
							xmlOutput: xmlOutput, 
							table: table, 
							parentElement: formElement, 
							propertiesElement: propertiesElement, 
							item: csi, 
							processContainers: true, 
							processEditControls: true, 
							forceReadOnly: readOnly, 
							itemTabIndex: tabIndex);
					}
					else if(bindingMember != "" & processEditControls) // property (entry field)
					{
						if (!table.Columns.Contains(bindingMember))
						{
							throw new Exception("Field '" + table.TableName + "." + bindingMember + 
								"' not found in a data structure for the form '" + panel.RootItem.Path + "'");
						}
						XmlElement propertyElement = AsPanelPropertyBuilder.CreateProperty(
							propertiesElement: propertiesElement, 
							propertyNamesElement: hideOnForm ? null : propertyNamesElement, 
							modelId: csi.Id, 
							bindingMember: bindingMember, 
							caption: caption, 
							gridCaption: gridCaption, 
							table: table,
							readOnly: readOnly, 
							left: left, 
							top: top, 
							width: width, 
							height: height, 
							captionLength: captionLength, 
							captionPosition: captionPosition, 
							gridColumnWidth: gridColumnWidth == "0" ? "" : gridColumnWidth,
							style: style, 
							tabIndex: tabIndex,
							fieldType: fieldType);

						switch(csi.ControlItem.Name)
						{
							case "ColorPicker":
								ColorPickerBuilder.Build(propertyElement);
								break;
							case "BlobControl":
								BlobControlBuilder.Build(propertyElement, csi);
								break;
							case "ImageBox":
								ImageBoxBuilder.Build(propertyElement, sourceType);
								break;
							case "AsTextBox": 
								TextBoxBuildDefinition buildDefinition = new TextBoxBuildDefinition(
									(OrigamDataType)table.Columns[bindingMember]
									.ExtendedProperties["OrigamDataType"]);
								buildDefinition.Dock = dock;
								buildDefinition.Multiline = multiline;
								buildDefinition.IsPassword = isPassword;
								buildDefinition.IsRichText = isRichText;
								buildDefinition.AllowTab = allowTab;
								buildDefinition.MaxLength = table.Columns[bindingMember].MaxLength;
                                buildDefinition.CustomNumberFormat = customNumericFormat;
								TextBoxBuilder.Build(propertyElement, buildDefinition);
								break;
							case "AsDateBox": 
								DateBoxBuilder.Build(propertyElement, format, customFormat);
								break;
							case "AsCheckBox":
								CheckBoxBuilder.Build(propertyElement, text);
								break;
							case "AsCombo":
							case "TagInput":
							case "Checklist":
								if(csi.ControlItem.Name == "AsCombo")
								{
									ComboBoxBuilder.Build(propertyElement, lookupId, showUniqueValues, bindingMember, table);
								}
								else if(csi.ControlItem.Name == "TagInput")
								{
									TagInputBuilder.BuildTagInput(propertyElement, lookupId, bindingMember, table);
								}
								else
								{
									TagInputBuilder.BuildChecklist(propertyElement, lookupId, bindingMember, columnWidth, table);
								}

								// set parameters
								XmlElement comboParametersElement = xmlOutput.Document.CreateElement("DropDownParameters");
								propertyElement.AppendChild(comboParametersElement);

								foreach(ColumnParameterMapping mapping in csi.ChildItemsByType(ColumnParameterMapping.CategoryConst))
								{
									XmlElement comboParamElement = xmlOutput.Document.CreateElement("ComboBoxParameterMapping");
									comboParametersElement.AppendChild(comboParamElement);

									comboParamElement.SetAttribute("ParameterName", mapping.Name);
									comboParamElement.SetAttribute("FieldName", mapping.ColumnName);
									propertyElement.SetAttribute("Cached", XmlConvert.ToString(false));
								}
								break;
							case "MultiColumnAdapterFieldWrapper" :
								MultiColumnAdapterFieldWrapperBuilder.Build(propertyElement, csi, controlMember);
								RenderPanel(panel, xmlOutput, table, propertyElement, propertyElement, csi, false, true, readOnly);
								XmlNode propertyNames = propertyElement.SelectSingleNode("PropertyNames");
								if (propertyNames == null)
								{
									throw new Exception("There are no widgets defined in MultiColumnAdapterFieldWrapper " + csi.Path);
								}
								propertyElement.RemoveChild(propertyNames);
								break;
							default:		// fallback: TextBox
								TextBoxBuilder.Build(propertyElement, 
									new TextBoxBuildDefinition(
									(OrigamDataType)table.Columns[bindingMember]
									.ExtendedProperties["OrigamDataType"]));
								break;
						}
						// The Id column was created earlier in AsPanelBuilder.cs
						// and is meant to be invisible.
						// If the Id column was added to the model in the Architect
						// and thus should be the invisible, the original invisible
						// Id column is removed. Duplicated Id column would create
						// problems in the client
						XmlElement zeroColumn = propertiesElement.FirstChild as XmlElement;
						if(propertyElement.GetAttribute("Id") == zeroColumn.GetAttribute("Id"))
						{
							propertiesElement.RemoveChild(zeroColumn);
						}
						if(csi.MultiColumnAdapterFieldCondition != Guid.Empty)
						{
							IParameterService parameterService 
								= ServiceManager.Services.GetService(typeof(IParameterService))
								as IParameterService;
							propertyElement.SetAttribute(
								"ControlPropertyValue",
	                            parameterService.GetParameterValue(
								csi.MultiColumnAdapterFieldCondition, 
								OrigamDataType.String) as string);
						}
                        if(csi.RequestSaveAfterChange)
                        {
                            propertyElement.SetAttribute(
                                "RequestSaveAfterChange", "true");
                        }
					}

				    if (lookupId != Guid.Empty)
				    {
				        xmlOutput.ContainedLookups.Add(lookupId);
                    }
				}
			}

			if(childrenElement.InnerXml == "")
			{
				parentElement.RemoveChild(childrenElement);
			}

			if(propertyNamesElement.InnerXml == "")
			{
				parentElement.RemoveChild(propertyNamesElement);
			}

			if(formExclusiveControlsElement.InnerXml == "")
			{
				parentElement.RemoveChild(formExclusiveControlsElement);
			}
		}
        /// <summary>
        /// https://docs.microsoft.com/cs-cz/dotnet/api/system.windows.forms.dockstyle?view=netframework-4.7.2
        /// </summary>
        /// <param name="intVal"></param>
        /// <returns></returns>
	    private static string ToWinFormsDockStyle(int intVal)
	    {
	        switch (intVal)
	        {
                case 2: return "Bottom";
	            case 5: return "Fill";
	            case 3: return "Left";
	            case 0: return "None";
	            case 4: return "Right";
	            case 1: return "Top";
                default: throw new Exception("Cannot convert value " +intVal+ " to System.Windows.Forms string");
            }
	    }

	    internal static string AddDataSource(Hashtable dataSources, DataTable table, string controlId, bool isIndependent)
		{
			string entityName = table.TableName;
			if(isIndependent)
			{
				entityName = controlId + "_" + entityName;
			}

			dataSources[entityName] = table;

			return entityName;
		}
	}
}
