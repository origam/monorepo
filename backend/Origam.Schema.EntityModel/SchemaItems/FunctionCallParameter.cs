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

using Origam.DA.Common;
using System;
using System.ComponentModel;
using Origam.DA.ObjectPersistence;
using System.Xml.Serialization;

namespace Origam.Schema.EntityModel
{
	/// <summary>
	/// Summary description for FunctionCallParameter.
	/// </summary>
	[SchemaItemDescription("Parameter", 15)]
    [HelpTopic("Function+Call+Field")]
	[XmlModelRoot(CategoryConst)]
    [ClassMetaVersion("6.0.0")]
	public class FunctionCallParameter : AbstractSchemaItem, ISchemaItemFactory
	{
		public const string CategoryConst = "FunctionCallParameter";

		public FunctionCallParameter() : base() {}

		public FunctionCallParameter(Guid schemaExtensionId) : base(schemaExtensionId) {}

		public FunctionCallParameter(Key primaryKey) : base(primaryKey)	{}

		#region Overriden AbstractDataEntityColumn Members

		public override string ItemType
		{
			get
			{
				return CategoryConst;
			}
		}

		public override string Icon
		{
			get
			{
				return "15";
			}
		}

		[Browsable(false)]
		public override bool UseFolders
		{
			get
			{
				return false;
			}
		}

		public override void GetExtraDependencies(System.Collections.ArrayList dependencies)
		{
			dependencies.Add(this.FunctionParameter);

			base.GetExtraDependencies (dependencies);
		}
		#endregion

		#region Properties
		public Guid FunctionParameterId;

		[NotNullModelElementRule()]
        [XmlReference("parameter", "FunctionParameterId")]
        public FunctionParameter FunctionParameter
		{
			get
			{
				ModelElementKey key = new ModelElementKey();
				key.Id = this.FunctionParameterId;

				return (FunctionParameter)this.PersistenceProvider.RetrieveInstance(typeof(FunctionParameter), key);
			}
			set
			{
				this.FunctionParameterId = (Guid)value.PrimaryKey["Id"];
			}
		}
		#endregion

		#region ISchemaItemFactory Members

		[Browsable(false)]
		public override Type[] NewItemTypes
		{
			get
			{
				return new Type[] {typeof(EntityColumnReference),
									typeof(FunctionCall),
									typeof(ParameterReference),
									typeof(DataConstantReference),
									typeof(EntityFilterReference),
									typeof(EntityFilterLookupReference)
								   };
			}
		}

		public override AbstractSchemaItem NewItem(Type type, Guid schemaExtensionId, SchemaItemGroup group)
		{
			AbstractSchemaItem item;

			if(type == typeof(EntityColumnReference))
			{
				item = new EntityColumnReference(schemaExtensionId);
			}
			else if(type == typeof(FunctionCall))
			{
				item = new FunctionCall(schemaExtensionId);
			}
			else if(type == typeof(ParameterReference))
			{
				item = new ParameterReference(schemaExtensionId);
			}
			else if(type == typeof(DataConstantReference))
			{
				item = new DataConstantReference(schemaExtensionId);
			}
			else if(type == typeof(EntityFilterReference))
			{
				item = new EntityFilterReference(schemaExtensionId);
			}
			else if(type == typeof(EntityFilterLookupReference))
			{
				item = new EntityFilterLookupReference(schemaExtensionId);
			}
			else
				throw new ArgumentOutOfRangeException("type", type, ResourceUtils.GetString("ErrorTableMappingItemUnknownType"));

			item.Group = group;
			item.PersistenceProvider = this.PersistenceProvider;
			item.IsAbstract = this.IsAbstract;
			this.ChildItems.Add(item);

			return item;
		}

		#endregion

		#region IComparable Members

		public override int CompareTo(object obj)
		{
			if(obj is FunctionCallParameter)
			{
				FunctionCallParameter par = obj as FunctionCallParameter;
				
				return this.FunctionParameter.OrdinalPosition.CompareTo(par.FunctionParameter.OrdinalPosition);
			}
			else
			{
				return base.CompareTo(obj);
			}
		}

		#endregion
	}
}
