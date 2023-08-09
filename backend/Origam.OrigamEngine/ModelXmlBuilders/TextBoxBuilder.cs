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

using System.Xml;

using Origam.Schema;

namespace Origam.OrigamEngine.ModelXmlBuilders
{
	/// <summary>
	/// Summary description for TextBoxBuilder.
	/// </summary>
	public class TextBoxBuilder
	{
		public static void Build(
			XmlElement propertyElement, TextBoxBuildDefinition buildDefinition)
		{
			if((buildDefinition.Type == OrigamDataType.Integer) 
			|| (buildDefinition.Type == OrigamDataType.Long) 
			|| (buildDefinition.Type == OrigamDataType.Float) 
			|| (buildDefinition.Type == OrigamDataType.Currency))
			{
				propertyElement.SetAttribute("Entity", 
					buildDefinition.Type.ToString());
				propertyElement.SetAttribute("Column", "Number");
                propertyElement.SetAttribute("CustomNumericFormat", 
                    buildDefinition.CustomNumberFormat);
			}
			else
			{
				propertyElement.SetAttribute("Entity", "String");
				propertyElement.SetAttribute("Column", "Text");
				propertyElement.SetAttribute("Dock", buildDefinition.Dock);
				propertyElement.SetAttribute("Multiline", 
					XmlConvert.ToString(buildDefinition.Multiline));
				propertyElement.SetAttribute("IsPassword", 
					XmlConvert.ToString(buildDefinition.IsPassword));
				propertyElement.SetAttribute("IsRichText", 
					XmlConvert.ToString(buildDefinition.IsRichText));
				propertyElement.SetAttribute("AllowTab",
					XmlConvert.ToString(buildDefinition.AllowTab));
				propertyElement.SetAttribute("MaxLength", 
					XmlConvert.ToString(buildDefinition.MaxLength));
			}
		}
	}

	public class TextBoxBuildDefinition
	{
		private OrigamDataType _Type;

		public OrigamDataType Type
		{
			get { return _Type; }
		}

		private string _Dock = "None";

		public string Dock
		{
			get { return _Dock; }
			set { _Dock = value; }
		}

		private bool _Multiline = false;

		public bool Multiline
		{
			get { return _Multiline; }
			set { _Multiline = value; }
		}

		private bool _IsPassword = false;

		public bool IsPassword
		{
			get { return _IsPassword; }
			set { _IsPassword = value; }
		}

		private bool _IsRichText = false;

		public bool IsRichText
		{
			get { return _IsRichText; }
			set { _IsRichText = value; }
		}

		private bool _AllowTab;

		public bool AllowTab
		{ 
			get { return _AllowTab; }
			set { _AllowTab = value; }
		}

		private int _MaxLength = 0;

		public int MaxLength
		{
			get { return _MaxLength; }
			set { _MaxLength = value; }
		}

		public TextBoxBuildDefinition(OrigamDataType type)
		{
			_Type = type;	
		}

        private string _CustomNumberFormat;

        public string CustomNumberFormat 
        {
            get { return _CustomNumberFormat; }
            set { _CustomNumberFormat = value; }
        }
    }
}
