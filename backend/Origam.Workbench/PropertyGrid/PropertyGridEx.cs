﻿#region license
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

using System.Collections;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Design;
using Origam.DA;
using System;
using Origam.Schema;
using Origam.Workbench.Commands;
using System.Reflection;
using System.Windows.Forms;
using Origam.Extensions;
using System.Linq;

namespace Origam.Workbench.PropertyGrid
{
    class PropertyGridEx : System.Windows.Forms.PropertyGrid
    {
        private static readonly PropertyValueUIItemInvokeHandler
            UIItemNullHandler = delegate { };
        private static readonly Image UIItemErrorImage =
            Properties.Resources.Exclamation_8x;
        private static readonly Image UIItemNavigateImage =
            Properties.Resources.Search_8x;
        private static readonly Image UIItemEditImage =
            Properties.Resources.Editor_8x;

        private readonly Action closeWindow;

        public PropertyGridEx(Action closeWindow): this()
        {
            this.closeWindow = closeWindow;
        }

        public PropertyGridEx()
        {
            Site = new SimpleSiteImpl();
            PropertyValueServiceImpl svc = new PropertyValueServiceImpl();
            svc.QueryPropertyUIValueItems += VerifyDataErrorInfo;
            ((SimpleSiteImpl)Site).AddService<IPropertyValueUIService>(svc);
        }

        void VerifyDataErrorInfo(ITypeDescriptorContext context,
            PropertyDescriptor propDesc, ArrayList valueUIItemList)
        {
            foreach (var item in propDesc.Attributes)
            {
                IModelElementRule rule = item as IModelElementRule;
                if (rule != null)
                {
                    Exception ex = rule.CheckRule(context.Instance, propDesc.Name);
                    if (ex != null)
                    {
                        valueUIItemList.Add(new
                            PropertyValueUIItem(UIItemErrorImage,
                            new PropertyValueUIItemInvokeHandler(UIItemNullHandler), ex.Message));
                    }
                }
            }
            var element = propDesc.GetValue(context.Instance) as AbstractSchemaItem;
            if (element != null)
            {
                var editHandler = new ModelElementEditHandler(closeWindow);
                valueUIItemList.Add(new
                    PropertyValueUIItem(UIItemEditImage,
                    editHandler.Run, "Double click to open " + element.Path));
            }
        }

        class ModelElementEditHandler
        {
            private readonly Action closeWindow;

            public ModelElementEditHandler(Action closeWindow)
            {
                this.closeWindow = closeWindow;
            }

            public void Run(ITypeDescriptorContext context,
                PropertyDescriptor descriptor, PropertyValueUIItem invokedItem)
            {
                try
                {
                    // navigate in model browser
                    var schemaBrowser = WorkbenchSingleton.Workbench.GetPad(typeof(SchemaBrowser)) as SchemaBrowser;
                    schemaBrowser.EbrSchemaBrowser.SelectItem(descriptor.GetValue(context.Instance) as AbstractSchemaItem);
                    ViewSchemaBrowserPad cmd = new ViewSchemaBrowserPad();
                    cmd.Run();
                    // edit
                    EditSchemaItem cmdEdit = new Commands.EditSchemaItem();
                    cmdEdit.Owner = descriptor.GetValue(context.Instance);
                    cmdEdit.Run();
                    closeWindow?.Invoke();
                }
                catch (Exception ex)
                {
                    Origam.UI.AsMessageBox.ShowError(null, ex.Message, "Error", ex);
                }
            }
            
        }


        internal void SetSplitter()
        {
            var flags = BindingFlags.GetField | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.InvokeMethod | BindingFlags.Public;
            FieldInfo View = this.GetType().BaseType.GetField("gridView", flags);
            Control controll = (Control)View.GetValue(this);
            MethodInfo methodInfo = controll.GetType().GetMethod("MoveSplitterTo", flags);
            GridItemCollection gridItemCollection = (GridItemCollection)controll.GetType().InvokeMember("GetAllGridEntries",
                flags, null, controll, null);
            int maxwidth = gridItemCollection
                .OfType<GridItem>()
                .OrderByDescending(gridItem=>gridItem.Label.Width(Font))
                .First().Label.Width(Font)+50;
            if (methodInfo != null)
                methodInfo.Invoke(controll, new object[] { maxwidth });
        }
    }
}
