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
using System.Data;
using System.Drawing;
using System.ComponentModel;
using System.Windows.Forms;

using Origam.Schema;
using Origam.Schema.GuiModel;
using Origam.Workbench.Services;
using Origam.Gui.UI;


namespace Origam.Gui.Win
{
	/// <summary>
	/// Summary description for AsTextBox.
	/// </summary>
	[ToolboxBitmap(typeof(AsTextBox))]
    public class AsTextBox : EnhancedTextBox, IAsCaptionControl, IAsControl,
		INotifyPropertyChanged, IAsGridEditor
    {
		Label _captionLabel = new Label();
		private readonly IPersistenceService persistence 
			= ServiceManager.Services.GetService(typeof(IPersistenceService)) as IPersistenceService;

	    public AsTextBox()
	    {
		    Click += (sender, args) 
			    => EditorClick?.Invoke(null, EventArgs.Empty);
		    DoubleClick += (sender, args) 
			    => EditorDoubleClick?.Invoke(null, EventArgs.Empty);
	    }

	    #region Handling base events

		protected override void InitLayout()
		{
			if(this.Disposing) return;

			this.BorderStyle = BorderStyle.Fixed3D;
			this.ScrollBars = ScrollBars.Vertical;
			this.AcceptsTab = true;

			base.InitLayout ();

			PaintCaption();
			ResetCaption();

			this.DataBindings.CollectionChanged += DataBindings_CollectionChanged;
		}

		protected override void Dispose(bool disposing)
		{
			if(disposing)
			{
				if(this.Parent != null && this.Parent.Controls.Contains(_captionLabel) && _captionLabel.IsDisposed == false)
				{
					this.Parent.Controls.Remove(_captionLabel);
				}

				_captionLabel = null;
			}
			try
			{
				base.Dispose (disposing);
			}
			catch{}
		}

		protected override void OnMove(EventArgs e)
		{
			base.OnMove (e);

			PaintCaption();
		}

		// when disposing, this throws an exception, we have to overcome this...
		protected override void OnValidated(EventArgs e)
		{
			if(! this.Disposing) base.OnValidated (e);
		}

		#endregion

		#region Properties
		
		public string DataMember
		{
			get
			{
				return null;
			}
			set
			{
			}
		}


		private int captionLength = 100;
		[Category("(ORIGAM)")]
		public int CaptionLength
		{
			get => captionLength;
			set
			{
				captionLength = value;
				PaintCaption();
			}
		}

		private bool hideOnForm = false;
		public bool HideOnForm
		{
			get => hideOnForm;
			set
			{
				hideOnForm = value;

				if(value && ! this.DesignMode)
				{
					this.Hide();
				}
			}
		}

	    [Description("Valid only for numeric data types. If specified, it will override default formatting for the given data type.")]
        public string CustomNumericFormat { get; set; } = "";

	    #endregion

		public override BindingContext BindingContext
		{
			get
			{
				return base.BindingContext;
			}
			set
			{
				//base.BindingContext = value;
			}
		}


		public override string Text
		{
			get => base.Text;
			set
			{
				base.Text = value;
                OnTextChanged(EventArgs.Empty);
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Text"));
			}
		}
		public bool IsPassword
		{
			get => this.PasswordChar == '*';
			set
			{
				if(value)
				{
					this.PasswordChar = '*';
				}
				else
				{
					this.PasswordChar = (char)0;
				}
			}
		}

	    [DefaultValue(false)]
		public bool IsRichText { get; set; } = false;

		[DefaultValue(false)]
		public bool AllowTab { get; set; } = false;

	    [Browsable(false)]
		public Guid StyleId { get; set; }

	    [TypeConverter(typeof(StylesConverter))]
		public UIStyle Style
		{
			get
			{
				return (UIStyle)persistence.SchemaProvider.RetrieveInstance(typeof(UIStyle), new ModelElementKey(this.StyleId));
			}
			set
			{
				this.StyleId = (value == null ? Guid.Empty : (Guid)value.PrimaryKey["Id"]);
			}
		}

		#region private methods
		private void PaintCaption()
		{
			if(_captionLabel == null | this.Parent == null | this.IsDisposed | this.Disposing) return;

			if(this.CaptionPosition == CaptionPosition.None)
			{
				if(this.Parent.Controls.Contains(_captionLabel))
				{
					this.Parent.Controls.Remove(_captionLabel);
				}
			}
			else
			{
				if(! this.Parent.Controls.Contains(_captionLabel))
				{
					this.Parent.Controls.Add(_captionLabel);
				}
			}

			this._captionLabel.Width = this.CaptionLength;
			this._captionLabel.BackColor = Color.Transparent;
			this._captionLabel.AutoSize = true;

			if(this.Visible || this.DesignMode)
			{
				switch(this.CaptionPosition)
				{
					case CaptionPosition.Left:
						this._captionLabel.Visible = true;
						this._captionLabel.Top = this.Top + 2;
						this._captionLabel.Left = this.Left - this.CaptionLength;
						break;

					case CaptionPosition.Right:
						this._captionLabel.Visible = true;
						this._captionLabel.Top = this.Top + 2;
						this._captionLabel.Left = this.Right;
						break;
				
					case CaptionPosition.Top:
						this._captionLabel.Visible = true;
						this._captionLabel.Top = this.Top - this._captionLabel.Height;
						this._captionLabel.Left = this.Left;
						break;
				
					case CaptionPosition.Bottom:
						this._captionLabel.Visible = true;
						this._captionLabel.Top = this.Top + this.Height;
						this._captionLabel.Left = this.Left;
						break;
				
					case CaptionPosition.None:
						this._captionLabel.Visible = false;
						break;
				}
			}
			else
			{
				this._captionLabel.Visible = false;
			}
		}

		private void DataBindings_CollectionChanged(object sender, CollectionChangeEventArgs e)
		{
			if (!this.DesignMode) return;
			if (!string.IsNullOrEmpty(this.Caption)) return;
			if (e.Element == null) return;
			if ((e.Element as Binding).PropertyName !=
			    this.DefaultBindableProperty) return;
			if(e.Action == CollectionChangeAction.Remove)
			{
				this._captionLabel.Text = "";
			}
			else
			{
				try
				{
					this._captionLabel.Text = ColumnCaption((e.Element as Binding));
				}
				catch
				{
					this._captionLabel.Text = "????";
				}
			}
		}

		private void ResetCaption()
		{
			if (!string.IsNullOrEmpty(this.Caption)) return;
			foreach(Binding binding in this.DataBindings)
			{
				if(binding.PropertyName == "Value")
					try
					{
						this._captionLabel.Text = ColumnCaption(binding);
					}
					catch
					{
						this._captionLabel.Text = "????";
					}
			}
		}

		private string TableName(DataSet ds, string dataMember)
		{
			// In case that dataMember is a path through relations, we find the last table
			// so we can take a caption out of it
			string tableName = "";
			if(dataMember.IndexOf(".") > 0)
			{
				string[] path = dataMember.Split(".".ToCharArray());
				DataTable table = ds.Tables[path[0]];
				for(int i = 1; i < path.Length - 1; i++)
				{
					table = table.ChildRelations[path[i]].ChildTable;
				}

				if(table != null)
				{
					tableName = table.TableName;
				}
			}
			else
				tableName = dataMember;

			return tableName;
		}

		private string ColumnCaption(Binding binding)
		{
			if(binding.DataSource is DataSet)
			{
				DataSet dataset = binding.DataSource as DataSet;
				// Get Table
				DataTable table = dataset.Tables[TableName(dataset, binding.BindingMemberInfo.BindingMember)];
				
				if(table != null)
				{
					if(table.Columns.Contains(binding.BindingMemberInfo.BindingField))
						return table.Columns[binding.BindingMemberInfo.BindingField].Caption;
				}
			}

			return binding.BindingMemberInfo.BindingField;
		}

		protected override void OnMouseWheel(MouseEventArgs e)
		{
			if(this.Parent is AsDataGrid grid)
			{
				grid.OnControlMouseWheel(e);
			}
			else
			{
				base.OnMouseWheel (e);
			}
		}
		#endregion

		#region IAsCaptionControl Members

	    [Category("(ORIGAM)")]
		[DefaultValue(100)] 
		[Description(CaptionDoc.GridColumnWidthDescription)]
        public int GridColumnWidth { get; set; }

	    [Category("(ORIGAM)")]
		public string GridColumnCaption { get; set; } = "";

	    string _caption = "";
		[Category("(ORIGAM)")]
		public string Caption
		{
			get => _caption;
			set
			{
			    _caption = value ?? "";
			    this._captionLabel.Text = _caption;
				ResetCaption();
			}
		}

		CaptionPosition _captionPosition = CaptionPosition.Left;
		[Category("(ORIGAM)")]
		public CaptionPosition CaptionPosition
		{
			get => _captionPosition;
			set
			{
				_captionPosition = value;
				PaintCaption();
			}
		}
		#endregion

		#region IAsControl Members

		public string DefaultBindableProperty => "Value";

	    #endregion

        public event PropertyChangedEventHandler PropertyChanged;
	    
	    public event EventHandler EditorClick;
	    public event EventHandler EditorDoubleClick;
    }

}
