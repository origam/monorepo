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
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Xml.Serialization;
using Origam.DA;
using Origam.DA.ObjectPersistence;
using Origam.UI;

namespace Origam.Schema
{
    /// <summary>
    /// This class makes a persistable base for any schema items. It has a special override for Key,
    /// it uses a ModelElementKey, which has strongly typed accessors to the primary key of
    /// any class that is contained under schema versions.
    /// </summary>
    [EntityName("SchemaItem", "TargetType")]
    [ClassMetaVersion("6.0.0")]
	public abstract class AbstractSchemaItem : AbstractPersistent, ISchemaItem, 
        IBrowserNode2, ISchemaItemFactory, ICloneable, IComparable, 
        ISchemaItemConvertible, INotifyPropertyChanged, IFilePersistent
    {
        //public const string NAMESPACE = "http://schemas.origam.com/*.*.*/model-element";
		public readonly object Lock = new object();
        private static string _regexSearch = new string(System.IO.Path.GetInvalidFileNameChars()) 
            + new string(System.IO.Path.GetInvalidPathChars());
        private static Regex _regex = new Regex(string.Format("[{0}]", Regex.Escape(_regexSearch)));

        #region Constructors
        public AbstractSchemaItem()
		{
			this.PrimaryKey = new ModelElementKey();
		}

		public AbstractSchemaItem(Guid extensionId) : this()
		{
			this.SchemaExtensionId = extensionId;
		}

		public AbstractSchemaItem(Key primaryKey) : base(primaryKey, new ModelElementKey().KeyArray) 
		{
		}

		#endregion

		#region Properties
		public override string ToString()
		{
			return this.Name;
		}

        [Browsable(false)]
		public IEnumerable<AbstractSchemaItem> ChildrenRecursive 
		{
			get
			{
				foreach (var child1 in ChildItems)
				{
					foreach (var child2 in GetChildrenRecursive(child1))
					{
						yield return child2;
					}
				}
			}
		}


		private ModelElementKey _oldPrimarykey = null;
		[Browsable(false)]
		public ModelElementKey OldPrimaryKey
		{
			get
			{
				return _oldPrimarykey;
			}
			set
			{
				_oldPrimarykey = value;
			}
		}

		[Category("(Info)")]
		[Description("Unique ID of this model element. Model elements are internally identified by ID's, not names.")]
		public new Guid Id
		{
			get
			{
				return (Guid)this.PrimaryKey["Id"];
			}
		}

		[Category("(Info)")]
		//[Browsable(false)]
		public string SchemaItemType
		{
			get
			{
				return this.GetType().ToString();
			}
		}

		private bool _isPersistable = true;
		[Browsable(false)]
		public bool IsPersistable
		{
			get
			{
				return _isPersistable;
			}
			set
			{
				_isPersistable = value;
			}
		}
		
		[Browsable(false)]
		public ArrayList ChildItemsRecursive
		{
			get
			{
				ArrayList items = new ArrayList();

				foreach(AbstractSchemaItem item in this.ChildItems)
				{
					items.Add(item);
					items.AddRange(GetChildItemsRecursive(item));
				}

				return items;
			}
		}

		[Browsable(false)]
		public string Path
		{
			get
			{
				return GetPath(this);
			}
		}
        
        [Browsable(false)]
	    public bool IsFileRootElement => FileParentId == Guid.Empty;

	    [Browsable(false)]
		public virtual bool UseFolders
		{
			get
			{
				return true;
			}
		}

		private bool _clearCacheOnPersist = true;
		[Browsable(false)]
		public bool ClearCacheOnPersist
		{
			get
			{
				return _clearCacheOnPersist;
			}
			set
			{
				_clearCacheOnPersist = value;
			}
		}

		private bool _throwEventOnPersist = true;
		[Browsable(false)]
		public bool ThrowEventOnPersist
		{
			get
			{
				return _throwEventOnPersist;
			}
			set
			{
				_throwEventOnPersist = value;
			}
		}

		private bool _neverRetrieveChildren = false;
		[Browsable(false)]
		public bool NeverRetrieveChildren
		{
			get => _neverRetrieveChildren;
			set => _neverRetrieveChildren = value;
		}
		#endregion

		#region Public Methods
        public string ModelDescription()
        {
            return this.GetType().SchemaItemDescription()?.Name;
        }

        public virtual void GetParameterReferences(AbstractSchemaItem parentItem, Hashtable list)
		{
			if(parentItem == null) return;

			foreach(AbstractSchemaItem item in parentItem.ChildItems)
			{
				if(item is ParameterReference & item.IsDeleted == false)
					if(!list.ContainsKey((item as ParameterReference).Parameter.Name))
						list.Add((item as ParameterReference).Parameter.Name, item);

				item.GetParameterReferences(item, list);
			}
		}

		public ArrayList GetUsage()
		{
            var referencelist = PersistenceProvider.GetReference(this.PrimaryKey);
            if (referencelist==null)
            {
                throw new Exception(ResourceUtils.GetString("ErrorBuildReferenceIndex"));
            }
            return referencelist;
        }

		public static void FinishConversion(AbstractSchemaItem source, AbstractSchemaItem converted)
		{
			converted.PrimaryKey["Id"] = source.PrimaryKey["Id"];
			converted.Name = source.Name;
			converted.IsAbstract = source.IsAbstract;
			// remember the parent because the newly created item
			// will be removed while removing the source
            AbstractSchemaItem parent = converted.ParentItem;
			// we have to delete first (also from the cache)
			source.DeleteChildItems = true;
			source.IsDeleted = true;
			source.Persist();
			// re-add the item to its parent
            parent.ChildItems.Add(converted); 
			converted.Persist();
		}

		/// <summary>
		/// Recursively changes extension on all child items of the provided schema item.
		/// </summary>
		/// <param name="extension"></param>
		public void SetExtensionRecursive(Package extension)
		{
			this.Package = extension;

			foreach(ISchemaItem child in this.ChildItems)
			{
				child.SetExtensionRecursive(extension);
			}
		}
		
		
		private IEnumerable<AbstractSchemaItem> GetChildrenRecursive(AbstractSchemaItem schemaItem)
		{
			foreach (var child1 in schemaItem.ChildItems)
			{
				foreach (var child2 in GetChildrenRecursive(child1))
				{
					yield return child2;
				}
			}
			yield return schemaItem;
		}


		public void InvalidateParentPersistenceCache()
		{
			AbstractSchemaItem parent = this.ParentItem;
			while(parent != null)
			{
				parent.PersistenceProvider.RemoveFromCache(parent);
				parent = parent.ParentItem;
			}
		}

		public void InvalidateChildrenPersistenceCache()
		{
			this.PersistenceProvider.RemoveFromCache(this);
			foreach(AbstractSchemaItem child in this.ChildItems)
			{
				child.InvalidateChildrenPersistenceCache();
			}
		}
		#endregion

		#region Overriden AbstractPersistent Members
		/// <summary>
		/// Overriden member from AbstractPersistent. When the item is derived from another item,
		/// we cannot save it.
		/// 
		/// We also persist all child items when persisting this one.
		/// </summary>
		public override void Persist()
		{
			if (DerivedFrom != null)
			{
				throw new InvalidOperationException(ResourceUtils.GetString("ErrorModifyDerived"));
			}
			if (IsAbstract == false && ParentItem != null && ParentItem.IsAbstract)
			{
				throw new InvalidOperationException(ResourceUtils.GetString("ErrorInheritable"));
			}
			if(IsPersistable == false) return;
			// check license policy for new items
			if(!IsPersisted)
			{
				CheckLicense();
			}
            AbstractSchemaItem _rootItemForRefresh = GetRootItem(this);
			var schemaItemCollection = ChildItems;
            if(!IsDeleted)
            {
                // PERSIST THE ELEMENT
                base.Persist();
            }
			// TAKE CARE ABOUT CHILD ITEMS
			ArrayList deletedItems = new ArrayList();
			if(PersistChildItems)
			{
				// We persist all child items
				foreach(AbstractSchemaItem item in schemaItemCollection)
				{
					if(item.DerivedFrom == null && IsPersistable)
					{
						// make sure that all child items are marked abstract if this one is
						if (IsAbstract) item.IsAbstract = true;
					    if (IsDeleted) item.IsDeleted = true;
						ChildItems.RemoveDeletedItems = false;
						item.ClearCacheOnPersist = this.ClearCacheOnPersist;
						item.ThrowEventOnPersist = false;
						item.Persist();
						item.ThrowEventOnPersist = true;
						ChildItems.RemoveDeletedItems = true;

						if(item.IsDeleted) deletedItems.Add(item);
					}
				}
                                
                foreach (AbstractSchemaItem item in deletedItems)
				{
					ChildItems.Remove(item);
				}
                RefreshDescendants(_rootItemForRefresh);
            }
            if(IsDeleted)
            {
                // PERSIST THE ELEMENT
                base.Persist();
            }
			// We persist any new ancestors
			foreach(SchemaItemAncestor ancestor in Ancestors)
			{
				ancestor.Persist();
			}
			_ancestorsPopulated = false;
            if(ClearCacheOnPersist)
            {
                ClearCache();
            }
			// after persisting invalidate all parents from the persistence cache
			// so they do not cache the old version of this child
			InvalidateParentPersistenceCache();
			if(ThrowEventOnPersist)
			{
				PersistenceProvider.OnTransactionEnded(this);
			}
		}

        private void RefreshDescendants(AbstractSchemaItem abstractSchemaItem)
        {
	        if (!abstractSchemaItem.Inheritable)
	        {
		        return;
	        }
	        foreach(var childItem in abstractSchemaItem.RootProvider.ChildItems)
	        { 
		        foreach (var itemAncestor in childItem.Ancestors)
		        {
			        if (itemAncestor.AncestorId == abstractSchemaItem.Id)
			        {
				        childItem.ClearCache();
			        }
		        }
	        }
        }

        public void ClearCache()
		{
			lock(Lock)
			{
				_ancestorsPopulated = false;
				_myAncestorsPopulated = false;
				_childItemsPopulated = false;
#if ORIGAM_CLIENT
				_hasParameterReferencesCached = false;
#endif
				_parameterReferences.Clear();
			
				if(_childItems != null)
				{
					_childItems.DeleteItemsOnClear = false;
					_childItems.Clear();
					_childItems.DeleteItemsOnClear = true;
				}
			}
		}

		public override void Refresh()
		{
			if(ClearCacheOnPersist)
			{
				ClearCache();
			}

			base.Refresh ();
		}

		public override IPersistent GetFreshItem()
		{
			IPersistent freshItem = base.GetFreshItem ();
			AbstractSchemaItem abstractItem = freshItem as AbstractSchemaItem;
			if(abstractItem != null)
			{
				abstractItem.ParentItem = this.ParentItem;
				abstractItem.RootProvider = this.RootProvider;
			}

			return freshItem;
		}


		private bool _persistChildItems = true;
		[Browsable(false)]
		public bool PersistChildItems
		{
			get
			{
				return _persistChildItems;
			}
			set
			{
				_persistChildItems = value;
			}
		}

		private bool _deleteChildItems = true;
		[Browsable(false)]
		public bool DeleteChildItems
		{
			get
			{
				return _deleteChildItems;
			}
			set
			{
				_deleteChildItems = value;
			}
		}

		public override bool IsDeleted
		{
			get
			{
				return base.IsDeleted;
			}
			set
			{
				if(this.DerivedFrom != null) throw new InvalidOperationException(ResourceUtils.GetString("ErrorModifyDerived"));

				if(this.DeleteChildItems)
				{
					// We delete all the kids (except of derived ones)
					int cnt = this.ChildItems.Count;

					for(int i = 0; i < cnt; i++)
					{
						if(this.ChildItems[i].DerivedFrom == null)
						{
							this.ChildItems[i].IsDeleted = true;
						}
					}

					// We delete all the ancestor references
					foreach(SchemaItemAncestor ancestor in this.Ancestors)
					{
						ancestor.IsDeleted = true;
					}
				}

				base.IsDeleted = value;

				//				if(this.IsDeleted)
				//				{
				//					// We delete from the dataset
				//					this.Persist();
				//				}
			}
		}

		public override bool UseObjectCache
		{
			get
			{
				// We cannot cache derived items, because they have multiple instances
				if(this.IsAbstract)
					return false;
				else
					return base.UseObjectCache;
			}
			set
			{
				base.UseObjectCache = value;
			}
		}

		#endregion

		#region ISchemaItem Members

		private string _name;

		[EntityColumn("Name")] 
		[Category("(Schema Item)")]
		[StringNotEmptyModelElementRule]
		[RefreshProperties(RefreshProperties.Repaint)]
        [XmlAttribute("name")]
		[Description("Name of the model element. The name is mainly used for giving the model elements a human readable name. In some cases the name is an identificator of the model element (e.g. for defining XML structures or for requesting constants from XSLT tranformations).")]
		public virtual string Name
		{
			get
			{
				if(_name == null) return null;
				return _name.Trim();
			}
			set
			{
				string originalName = _name;
				_name = value;
				OnNameChanged(originalName);
			}
		}

		public virtual void OnNameChanged(string originalName)
		{
		}

		[Category("(Schema Item)"), DefaultValue(false)]
		[Description("Indicates if this model element allows to be inherited by another model element. If set to true, it is possible e.g. to reuse an entity definition in other entities. If an element is inheritable, all child elements must be set to Inheritable=true.")]
		public bool Inheritable
		{
			get
			{
				return IsAbstract;
			}
			set
			{
				IsAbstract = value;
			}
		}

		private bool _isAbstract;
		[EntityColumn("IsAbstract")]
		[Browsable(false)]
        [XmlAttribute("abstract")]
		public bool IsAbstract
		{
			get
			{
				return _isAbstract;
			}
			set
			{
				_isAbstract = value;
			}
		}

	    [EntityColumn("refParentItemId")]
        [Browsable(false)]
	    public Guid ParentItemId { get; set; }

	    private AbstractSchemaItem _parentItem = null;

		[Browsable(false)]
		public AbstractSchemaItem ParentItem
		{
			get
			{
				if(_parentItem != null) return _parentItem;

				ModelElementKey key = new ModelElementKey();
				key.Id = this.ParentItemId;

				return (AbstractSchemaItem)this.PersistenceProvider.RetrieveInstance(typeof(AbstractSchemaItem), key);
			}
			set
			{
				_parentItem = value;

				if(value == null)
				{
					this.ParentItemId = Guid.Empty;
				}
				else
				{
					this.ParentItemId = (Guid)value.PrimaryKey["Id"];
				}
			}
		}
		[Browsable(false)]
		public AbstractSchemaItem RootItem
		{
			get
			{
				return this.GetRootItem(this);
			}
		}

		private AbstractSchemaItem GetRootItem(AbstractSchemaItem parentItem)
		{
			if(parentItem.ParentItem == null)
				return parentItem;
			else
				return GetRootItem(parentItem.ParentItem);
		}

	    [EntityColumn("refSchemaExtensionId")]
	    [XmlParent(typeof(Package))]
        [Browsable(false)]
	    public Guid SchemaExtensionId { get; set; }
    

		[Category("(Info)")]
		[Description("Name of the package this model element belongs to.")]
		public string PackageName
		{
			get
			{
				return this.Package.ToString();
			}
		}

		[Browsable(false)]
		public Package Package
		{
			get
			{
				ModelElementKey key = new ModelElementKey();
				key.Id = this.SchemaExtensionId;

				return (Package)this.PersistenceProvider.RetrieveInstance(typeof(Package), key);
			}
			set
			{
				this.SchemaExtensionId = (Guid)value.PrimaryKey["Id"];
			}
		}

		private bool _ancestorsPopulated = false;
		private SchemaItemAncestorCollection _ancestors = new SchemaItemAncestorCollection();
		private Hashtable _childItemsById = new Hashtable();
		private Hashtable _childItemsByType = new Hashtable();
		private Hashtable _childItemsByName = new Hashtable();

		[Browsable(false)] 
		public SchemaItemAncestorCollection AllAncestors
		{
			get
			{
				if(! _ancestorsPopulated)
				{
					lock(Lock)
					{
						if(! _ancestorsPopulated)
						{
							_ancestors.Clear();

							// Get children
							List<SchemaItemAncestor> myAncestors = 
                                PersistenceProvider.RetrieveListByParent<SchemaItemAncestor>(
                                PrimaryKey, "SchemaItem", "SchemaItemAncestor", true);

							// We remove any ancestors from superior model packages (i.e. ancestors we could not load)
							List<SchemaItemAncestor> toDelete = new List<SchemaItemAncestor>();
							foreach(SchemaItemAncestor anc in myAncestors)
							{
								if(anc.Ancestor == null)
								{
									toDelete.Add(anc);
								}
							}

							foreach(SchemaItemAncestor del in toDelete)
							{
								myAncestors.Remove(del);
							}

							toDelete.Clear();
							toDelete = null;

							// Set parent for each child
							foreach(SchemaItemAncestor anc in myAncestors)
							{
								anc.SchemaItem = this;
								if(! _ancestors.Contains(anc))
								{
									_ancestors.Add(anc);

									// Also add all ancestors of this ancestor
									try
									{
										foreach(SchemaItemAncestor childAncestor in anc.Ancestor.AllAncestors)
										{
											if(! _ancestors.Contains(childAncestor))
											{
												_ancestors.Add(childAncestor);
											}
										}
									}
									catch{}
								}
							}

							_ancestorsPopulated = true;
						}
					}
				}

				return _ancestors;
			}
		}

		private bool _myAncestorsPopulated = false;
		private SchemaItemAncestorCollection _myAncestors = new SchemaItemAncestorCollection();

		[Category("(Schema Item)")]
		[Description("Inherited model elements. E.g. inherited entities from which you want to share fields, filters, etc. In order to inherit a model element, the inherited model element has to be set to Inheritable=true.")]
        [TypeConverter(typeof(SchemaItemAncestorConverter))]
        public SchemaItemAncestorCollection Ancestors
		{
			get
			{
				if(! _myAncestorsPopulated)
				{
					lock(Lock)
					{
						if(! _myAncestorsPopulated)
						{
							_myAncestors.Clear();

							// Get children
							List<SchemaItemAncestor> myAncestors = 
								this.PersistenceProvider
									.RetrieveListByParent<SchemaItemAncestor>(
										this.PrimaryKey, "SchemaItem", "SchemaItemAncestor", this.UseObjectCache);

							// Set parent for each child
							foreach(SchemaItemAncestor anc in myAncestors)
							{
								anc.SchemaItem = this;
								_myAncestors.Add(anc);
							}

							_myAncestorsPopulated = true;
						}
					}
				}

				return _myAncestors;
			}
		}

	    [Browsable(false)]
	    public List<SchemaItemGroup> ChildGroups => new List<SchemaItemGroup>();

		[Browsable(false)]
		public bool HasChildItems => ChildItems.Count > 0;

	    public bool HasChildItemsByType(string itemType)
		{
			return this.ChildItemsByType(itemType).Count > 0;
		}

		public bool HasChildItemsByGroup(SchemaItemGroup group)
		{
			return this.ChildItemsByGroup(group).Count > 0;
		}

		public AbstractSchemaItem GetChildByName(string name, string itemType)
		{
			foreach(AbstractSchemaItem item in this.ChildItems)
			{
				if(item.Name == name & item.ItemType == itemType)
				{
					return item;
				}
			}

			return null;
		}

		public AbstractSchemaItem GetChildByName(string name)
		{
#if ORIGAM_CLIENT
			AbstractSchemaItem result = GetItemFromCache(name);

			if(result != null)
			{
				return result;
			}
			// if we did not find in cache, we try one by one
#endif
			foreach(AbstractSchemaItem item in this.ChildItems)
			{
				if(item.Name == name) 
				{
					return item;
				}
			}

			return null;
		}

		public AbstractSchemaItem GetChildById(Guid id)
		{
#if ORIGAM_CLIENT
			AbstractSchemaItem result = GetItemFromCache(id);

			if(result != null)
			{
				return result;
			}
			// if we did not find in cache, we try one by one
#endif
			foreach(AbstractSchemaItem item in this.ChildItems)
			{
				if(item.PrimaryKey["Id"].Equals(id)) 
				{
					return item;
				}
			}

			return null;
		}

		public AbstractSchemaItem GetChildByIdRecursive(Guid id)
		{
			// first try from the current level
			AbstractSchemaItem result = GetChildById(id);

			if(result == null)
			{
				// if not found, try recursive
				foreach(AbstractSchemaItem child in this.ChildItems)
				{
					result = child.GetChildByIdRecursive(id);

					if(result != null) break;
				}
			}

			return result;
		}

		SchemaItemCollection _childItems;
		bool _childItemsPopulated = false;
		
		[Browsable(false)]
		public virtual SchemaItemCollection ChildItems
		{
			get
			{
				// We late-populate the child items collection on first access
				InitializeItemCache();
		
				return _childItems;
			}
		}

		private void InitializeItemCache()
		{
			if(! _childItemsPopulated)
			{
				lock(Lock)
				{
					if(! _childItemsPopulated)
					{
						if(_childItems == null)
						{
							_childItems = new SchemaItemCollection(this.PersistenceProvider, this.RootProvider, this);
						}

						_childItems.Clear();
						_childItems.AddRange(this.GetChildItems(this));

						foreach(SchemaItemAncestor anc in this.AllAncestors)
						{
							
							_childItems.AddRange(this.GetChildItems(anc.Ancestor));
							
						}

#if ORIGAM_CLIENT
						_childItemsById.Clear();
						_childItemsByName.Clear();
						_childItemsByType.Clear();

						foreach(ISchemaItem item in _childItems)
						{
							AddItemToCache(item);
						}
#endif
						_childItemsPopulated = true;
					}
				}
			}
		}

		private void AddItemToCache(ISchemaItem item)
		{
			_childItemsById[item.PrimaryKey["Id"]] = item;
			_childItemsByName[item.Name] = item;
			AddItemToTypeCache(item);
		}

		private int _childItemsTypeCacheCount = 0;

		private void AddItemToTypeCache(ISchemaItem item)
		{
			if(! _childItemsByType.Contains(item.ItemType))
			{
				_childItemsByType.Add(item.ItemType, new ArrayList());
			}

			(_childItemsByType[item.ItemType] as ArrayList).Add(item);
			
			_childItemsTypeCacheCount++;
		}

		private AbstractSchemaItem GetItemFromCache(string name)
		{
			// initialize the cache
			InitializeItemCache();

			if(_childItemsByName.Contains(name))
			{
				return _childItemsByName[name] as AbstractSchemaItem;
			}
			else
			{
				return null;
			}
		}

		private AbstractSchemaItem GetItemFromCache(Guid id)
		{
			// initialize the cache
			InitializeItemCache();

			if(_childItemsById.Contains(id))
			{
				return _childItemsById[id] as AbstractSchemaItem;
			}
			else
			{
				return null;
			}
		}

		private ArrayList GetItemsFromCache(string itemType)
		{
			// initialize the cache
			InitializeItemCache();

			if(_childItemsByType.Contains(itemType))
			{
				// we copy the array, because of multithreading (collection may change while
				// another thread is running)
				return new ArrayList(_childItemsByType[itemType] as ArrayList);
			}
			else
			{
				return new ArrayList();
			}
		}

		[Browsable(false)]
		public ArrayList Parameters
		{
			get
			{
				return this.ChildItemsByType(SchemaItemParameter.CategoryConst);
			}
		}

		Hashtable _parameterReferences = new Hashtable();

		[Category("(Schema Item)")]
		[Browsable(false)]
		public virtual Hashtable ParameterReferences
		{
			get
			{
#if ORIGAM_CLIENT
				if(!_hasParameterReferencesCached)
				{
					lock(Lock)
					{
						if(!_hasParameterReferencesCached)
						{
#else
					lock(Lock)
					{
#endif
							_parameterReferences.Clear();
							GetParameterReferences(this, _parameterReferences);
#if ORIGAM_CLIENT
							_hasParameterReferences = (_parameterReferences.Count > 0);
							_hasParameterReferencesCached = true;
						}
					}
#endif
				}

				return _parameterReferences;
			}
		}

#if ORIGAM_CLIENT
		private bool _hasParameterReferencesCached = false;
#endif
		private bool _hasParameterReferences = false;
		
		[Browsable(false)]
		public bool HasParameterReferences
		{
			get
			{
#if ORIGAM_CLIENT
				if(! _hasParameterReferencesCached)
				{
					lock(Lock)
					{
						if(! _hasParameterReferencesCached)
						{
#else
						lock(Lock)
						{
#endif
							_hasParameterReferences = (this.ParameterReferences.Count > 0);
#if ORIGAM_CLIENT
							_hasParameterReferencesCached = true;
						}
					}
#endif
				}

				return _hasParameterReferences;
			}
		}
		

		public ArrayList ChildItemsByType(string itemType)
		{
#if ORIGAM_CLIENT
			// if the number of items is different than cached, we go through the whole collection
			if(_childItemsTypeCacheCount == this.ChildItems.Count) 
			{
				return GetItemsFromCache(itemType);
			}
#endif
			ArrayList list = new ArrayList();

			foreach(AbstractSchemaItem item in this.ChildItems)
			{
				if(item.ItemType == itemType)
					list.Add(item);
			}
			
			return list;
		}

		public ArrayList ChildItemsByTypeRecursive(string itemType)
		{
			ArrayList list = new ArrayList();

			foreach(AbstractSchemaItem item in this.ChildItemsRecursive)
			{
				if(item.ItemType == itemType)
					list.Add(item);
			}
			
			return list;
		}

		public ArrayList ChildItemsByGroup(SchemaItemGroup group)
		{
			ArrayList list = new ArrayList();

			foreach(AbstractSchemaItem item in this.ChildItems)
			{
				if((item.Group == null && group == null) || item.Group.PrimaryKey.Equals(group.PrimaryKey))
					list.Add(item);
			}
			
			return list;
		}

		[Category("(Info)")]
		[EntityColumn("ItemType")]
		[Description("Type of the model element.")]
		public abstract string ItemType{get;}

		[EntityColumn("refSchemaItemGroupId")]
        [XmlParent(typeof(SchemaItemGroup))]
        public Guid GroupId;

		//[Category("(Info)")]
		[Browsable(false)]
		public SchemaItemGroup Group
		{
			get
			{
				ModelElementKey key = new ModelElementKey();
				key.Id = this.GroupId;

				try
				{
					return (SchemaItemGroup)this.PersistenceProvider.RetrieveInstance(typeof(SchemaItemGroup), key);
				}
				catch(Exception ex)
				{
					throw new Exception(ResourceUtils.GetString("ErrorGroupNotFound", this.ItemType, this.Path), ex);
				}
			}
			set
			{
				if(value == null)
				{
					this.GroupId = Guid.Empty;
				}
				else
				{
					this.GroupId = (Guid)value.PrimaryKey["Id"];
				}
			}
		}

		private AbstractSchemaItem _derivedFrom = null;
		[Browsable(false)] 
		public AbstractSchemaItem DerivedFrom
		{
			get
			{
				return _derivedFrom;
			}
			set
			{
				if(_derivedFrom != null & value == null) throw new InvalidOperationException(ResourceUtils.GetString("ErrorRevertDerived"));

				_derivedFrom = value;
			}
		}

		private ISchemaItemProvider _rootProvider;
		[Browsable(false)] 
		public ISchemaItemProvider RootProvider
		{
			get
			{
				return _rootProvider;
			}
			set
			{
				_rootProvider = value;
			}
		}

		[Browsable(false)]
		public ArrayList GetDependencies(bool ignoreErrors)
		{
			ArrayList dependencies = new ArrayList();
			foreach(SchemaItemAncestor anc in this.Ancestors)
			{
				dependencies.Add(anc.Ancestor);
			}

			try
			{
				GetExtraDependencies(dependencies);
			}
			catch
			{
				if(!ignoreErrors) throw;
			}
			return dependencies;
		}

		public virtual void GetExtraDependencies(ArrayList dependencies) {}
		
		public virtual void UpdateReferences()
		{
			foreach(ISchemaItem childItem in this.ChildItems)
			{
				if(childItem.DerivedFrom == null)
				{
					childItem.UpdateReferences();
				}
			}
		}

		[Browsable(false)]
		public bool AutoCreateFolder
		{
			get
			{
				return false;
			}
		}
        #endregion

        #region IPersistent2 Members


        [Category("(Info)")]
        public string RelativeFilePath
        {
            get
            {
                SchemaItemGroup group = this.RootItem.Group;
                string groupPath = "";
                if (group != null)
                {
                    groupPath = group.Path.Replace("/", "\\") + "\\";
                }
                return Package.Name + "\\" + RootItem.ItemType
                    + "\\" + groupPath + RemoveIllegalCharactersFromPath(RootItem.Name) + PersistenceFiles.Extension;
            }
        }

        private string RemoveIllegalCharactersFromPath(string text)
        {
            if (!string.IsNullOrEmpty(text))
            {
                return _regex.Replace(text, "");
            }
            return text;
        }

        [Browsable(false)]
        public Guid FileParentId
        {
            get => this.ParentItemId;
	        set => ParentItemId = value;
        }

        [Browsable(false)]
        public bool IsFolder
        {
            get
            {
                return false;
            }
        }

        [Browsable(false)]
        public IDictionary<string, Guid> ParentFolderIds =>
	        new Dictionary<string, Guid>
	        {
		        {
			        CategoryFactory.Create(typeof(Package)),
			        SchemaExtensionId
		        },
		        {
			        CategoryFactory.Create(typeof(SchemaItemGroup)),
			        GroupId
		        }
	        };
	    #endregion

        #region Private Methods
        private void CheckLicense()
		{
			AbstractSchemaItem item = this;

			ISchemaItemProvider provider = item.ParentItem == null ? item.RootProvider : item.ParentItem as ISchemaItemProvider;

			if(provider == null)
			{
				System.Diagnostics.Debug.Fail("Both ParentItem and RootProvider not specified");
				return;
			}

			long limit = LicensePolicy.ModelElementLimit(item.ItemType);

			if(limit != -1)
			{
				long count = 0;
				foreach(ISchemaItem child in provider.ChildItems)
				{
					// only count items of same type, from the same package and not derived
					if(child.ItemType == item.ItemType 
						& child.Package.PrimaryKey.Equals(item.Package.PrimaryKey)
						& child.DerivedFrom == null
						)
					{
						count++;
					}
				}

				if(count > limit)
				{
					throw new Exception(ResourceUtils.GetString("ErrorAddingOnly", limit.ToString(), item.ItemType));
				}
			}
		}

		private ArrayList GetChildItemsRecursive(AbstractSchemaItem parentItem)
		{
			ArrayList items = new ArrayList();

			foreach(AbstractSchemaItem childItem in parentItem.ChildItems)
			{
				items.Add(childItem);
				items.AddRange(GetChildItemsRecursive(childItem));
			}

			return items;
		}

		private string GetPath(AbstractSchemaItem item)
		{
			if(this.ParentItem == null)
			{
				return this.Name;
			}
			else
			{
				return this.ParentItem.Path + "/" + this.Name;
			}
		}

		private SchemaItemCollection GetChildItems(AbstractSchemaItem parentItem)
		{
			SchemaItemCollection col = new SchemaItemCollection(this.PersistenceProvider, this.RootProvider, parentItem);
            // Get children if we're allowed to do so
            if(!NeverRetrieveChildren)
            {
				// If this object does not use cache, all child items won't use it as well.
				// This is because when we edit an object and then we cancel editing, we don't want to keep any
				// updated-not-cancelled items in the cache.
				bool useCache = parentItem.UseObjectCache;
				List<AbstractSchemaItem> list = 
					this.PersistenceProvider
						.RetrieveListByParent<AbstractSchemaItem>(
							parentItem.PrimaryKey, "SchemaItem", "ChildSchemaItem", useCache);

				foreach(AbstractSchemaItem si in list)
				{
					col.Add(si);
				}
            }
			return col;
		}

		protected override void Dispose(bool disposing)
		{
			if(disposing)
			{
				_ancestors.Clear();
				_childItemsById.Clear();
				_childItemsByType.Clear();
				_childItemsByName.Clear();
				_childItems.Dispose();
				_derivedFrom = null;
				_myAncestors.Clear();
				_parentItem = null;
			}

			base.Dispose (disposing);
		}

        public virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, 
                new PropertyChangedEventArgs(propertyName));
        }
		#endregion

		#region IBrowserNode Members
		
		[Browsable(false)]
		public bool HasChildNodes
		{
			get
			{
				return this.ChildNodes().Count > 0;
			}
		}

		[Browsable(false)] 
		public virtual bool CanRename
		{
			get
			{
				// allow renaming only when the item has been persisted already
				return this.IsPersisted;
			}
		}

		[Browsable(false)] 
		public virtual bool CanDelete
		{
			get
			{
				return true;
			}
		}

		public void Delete()
		{
            if (GetUsage().Count == 0)
			{
				this.IsDeleted = true;
				this.Persist();
			}
			else
			{
				throw new InvalidOperationException(ResourceUtils.GetString("ErrorDeleteReferenced", this.Path));
			}
		}

		[Browsable(false)] 
		public virtual BrowserNodeCollection ChildNodes()
		{
			Hashtable folders = new Hashtable();

			BrowserNodeCollection col = new BrowserNodeCollection();

			// All groups
			foreach(SchemaItemGroup group in this.ChildGroups)
			{
				col.Add(group);
			}

			if(this.AllAncestors.Count > 0)
			{
				NonpersistentSchemaItemNode folder = new NonpersistentSchemaItemNode();
				folder.NodeText = "_Ancestors";
				folder.ParentNode = this;
				col.Add(folder);
			}

			// All other child items
			foreach(AbstractSchemaItem item in this.ChildItems)
			{
				// and only own (not derived) items, they will be returned by SchemaItemAncestor
				if(item.DerivedFrom == null & item.IsDeleted == false)
				{
					if(this.UseFolders)
					{
						SchemaItemDescriptionAttribute attr = item.GetType().SchemaItemDescription();
						string description = attr == null ? item.ItemType : attr.FolderName;
						if(description == null) description = item.ItemType;

						if(! folders.Contains(description))
						{
							NonpersistentSchemaItemNode folder = new NonpersistentSchemaItemNode();
							folder.ParentNode = this;
							folder.NodeText = description;
							col.Add(folder);
							folders.Add(description, folder);
						}

						// we just add the folders here, the items in the nonpersistent folder will
						// be returned by the folder itself (filtered from this item's child items
					}
					else
					{
						col.Add(item);
					}
				}
			}

			return col;
		}

		[Browsable(false)] 
		public virtual string NodeText
		{
			get
			{
				return this.Name;
			}
			set
			{
				this.Name = value;
				this.Persist();
			}
		}

		[Browsable(false)] 
		public string NodeId
		{
			get
			{
				return this.Id.ToString();
			}
		}

		[Browsable(false)] 
		public bool Hide
		{
			get
			{
				return !this.IsPersisted;
			}
			set
			{
				throw new InvalidOperationException(ResourceUtils.GetString("ErrorSetHide"));
			}
		}

		[Browsable(false)] 
		public virtual string Icon
        {
            get
            {
                return this.GetType().SchemaItemIcon()?.ToString();
            }
        }

	    [Browsable(false)]
	    public virtual byte[] NodeImage
		{
			get
			{
				return null;
			}
		}

        [Browsable(false)]
        public virtual string FontStyle
        {
            get
            {
                return "Regular";
            }
        }

		public virtual bool CanMove(IBrowserNode2 newNode)
		{
			return false;
		}

		[Browsable(false)]
		public IBrowserNode2 ParentNode
		{
			get
			{
				return this.ParentItem;
			}
			set
			{
				if(value is AbstractSchemaItem)
				{
					// first remove us from the parent's child items
					if(this.ParentItem != null && this.ParentItem.ChildItems.Contains(this))
					{
						this.ParentItem.ChildItems.Remove(this);
					}

					this.ParentItem = value as AbstractSchemaItem;

					// then add us as the child of the new item
					(value as ISchemaItem).ChildItems.Add(this);
				}
				else
				{
					throw new ArgumentOutOfRangeException("ParentItem", value, ResourceUtils.GetString("ErrorNotAbstractSchemaItem"));
				}
			}
		}

		#endregion

		#region ISchemaItemFactory Members
		private ArrayList _childItemTypes = new ArrayList();
		[Browsable(false)]
		public ArrayList ChildItemTypes
		{
			get
			{
				foreach(Type[] entry in ExtensionChildItemTypes)
				{
					if((entry[0].Equals(this.GetType()) || this.GetType().IsSubclassOf((Type)entry[0])) && ! _childItemTypes.Contains( entry[1]))
					{
						_childItemTypes.Add(entry[1]);
					}
				}

				return _childItemTypes;
			}
		}

		private static ArrayList _extensionChildItemTypes = new ArrayList();

	    public event PropertyChangedEventHandler PropertyChanged;

		public static ArrayList ExtensionChildItemTypes
		{
			get
			{
				return _extensionChildItemTypes;
			}
		}

		public virtual AbstractSchemaItem NewItem(Type type, Guid schemaExtensionId, SchemaItemGroup group)
		{
			AbstractSchemaItem item;

			if(_childItemTypes.Contains(type))
			{
				item = (AbstractSchemaItem)type.GetConstructor(new Type[] {typeof(Guid)}).Invoke(new object[] {schemaExtensionId});;
			}
			else
				throw new ArgumentOutOfRangeException("type", type, ResourceUtils.GetString("ErrorTypeNotSupported", this.GetType().Name));

			item.Group = group;
			item.PersistenceProvider = this.PersistenceProvider;
			item.IsAbstract = this.IsAbstract;
			this.ChildItems.Add(item);
#if ORIGAM_CLIENT
			AddItemToTypeCache(item);
#endif
			ItemCreated?.Invoke(item);
			return item;
		}

		public virtual SchemaItemGroup NewGroup(Guid schemaExtensionId, string groupName)
		{
			return null;
		}

		[Browsable(false)]
		public virtual Type[] NewItemTypes
		{
			get
			{
				return (Type[])_childItemTypes.ToArray(typeof(Type));
			}
		}

		[Browsable(false)]
		public virtual IList<string> NewTypeNames
		{
			get
			{
				return new List<string>();
			}
		}

		/// <summary>
		/// By default all NewItemTypes are nameable. Override if only a subset of types can
		/// be populated with NewTypeNames.
		/// </summary>
		[Browsable(false)]
		public virtual Type[] NameableTypes
		{
			get
			{
				return NewItemTypes;
			}
		}

		public event Action<ISchemaItem> ItemCreated;

		#endregion

		#region Events
//		public event EventHandler SchemaItemDeleted;
//		void OnSchemaItemDeleted(EventArgs e)
//		{
//			if (SchemaItemDeleted != null) 
//			{
//				SchemaItemDeleted(this, e);
//			}
//		}

//		public event EventHandler SchemaItemChanged;
//		void OnSchemaItemChanged(EventArgs e)
//		{
//			if (SchemaItemChanged != null) 
//			{
//				if(this.Group == null)
//				{
//					SchemaItemChanged(this, e);
//				}
//				else
//				{
//					SchemaItemChanged(this.Group, e);
//				}
//			}
//		}
		#endregion

		#region Event Handlers
//		private void AbstractSchemaItem_Changed(object sender, EventArgs e)
//		{
//			OnSchemaItemChanged(e);
//		}

//		private void AbstractSchemaItem_Deleted(object sender, EventArgs e)
//		{
//			OnSchemaItemDeleted(e);
//		}
//		private void _childItems_AfterItemAdded(object sender, SchemaItemCollectionEventArgs e)
//		{
//			OnSchemaItemChanged(e);
//		}
//
//		private void _childItems_AfterItemRemoved(object sender, SchemaItemCollectionEventArgs e)
//		{
//			OnSchemaItemChanged(e);
//		}
		#endregion

		#region ICloneable Members
		public virtual object Clone()
		{
			return this.Clone(false);
		}

		public object Clone(bool keepKeys)
		{
			if(!this.IsPersisted)
			{
				throw new InvalidOperationException(ResourceUtils.GetString("ErrorCloneNotPersisted"));
			}

			AbstractSchemaItem newItem = this.PersistenceProvider.RetrieveInstance(this.GetType(), this.PrimaryKey, false) as AbstractSchemaItem;
			// we preserve current primary key, so references can be updated later
			newItem.OldPrimaryKey = new ModelElementKey((Guid)this.PrimaryKey["Id"]);
			// if we're keeping keys (deep clone),
			// flag that we don't want to load children,
			// because we're going to inject them
			if(keepKeys)
			{
				newItem.NeverRetrieveChildren = true;
			}
			// we create a new unique primary key
			else
			{
				newItem.PrimaryKey = new ModelElementKey(Guid.NewGuid());
			}
            // in case that for any reason child items were populated already 
            // in the item's construction, we clear them
			newItem.ChildItems.DeleteItemsOnClear = false;
			newItem.ChildItems.Clear();
			newItem.ChildItems.DeleteItemsOnClear = true;
			newItem.ClearCache();
			
			newItem.ParentItem = this.ParentItem;
			newItem.RootProvider = this.RootProvider;
			newItem.IsPersisted = false;
			
			if(this.ParentItem == null)
			{
				this.RootProvider.ChildItems.Add(newItem);
			}
			

			foreach(SchemaItemAncestor ancestor in this.Ancestors)
			{
				SchemaItemAncestor newAncestor = ancestor.Clone() as SchemaItemAncestor;
				newAncestor.SchemaItem = newItem;

				newItem.Ancestors.Add(newAncestor);
			}

			foreach(ISchemaItem childItem in this.ChildItems)
			{
				if(childItem.DerivedFrom == null) // we do not copy derived items directly, they will be derived
				{
					AbstractSchemaItem newChild = childItem.Clone(keepKeys) as AbstractSchemaItem;
					newChild.ParentItem = newItem;
					newItem.ChildItems.Add(newChild);
				}
			}
			return newItem;
		}
		#endregion

		#region IComparable Members

		public virtual int CompareTo(object obj)
		{
			ISchemaItem item = obj as ISchemaItem;
			SchemaItemGroup group = obj as SchemaItemGroup;
            string name = this.Name ?? "";
			if(item != null)
			{
				return name.CompareTo(item.Name);
			}
			else if(group != null)
			{
				return 1;
			}
			else
			{
				throw new InvalidCastException();
			}
		}

		#endregion

		#region ISchemaItemConvertible Members

		public virtual ISchemaItem ConvertTo(Type type)
		{
			throw new Exception(ResourceUtils.GetString("ErrorConvertTo", type.ToString()));
		}

		public virtual bool CanConvertTo(Type type)
		{
			return false;
		}

		#endregion

		public void Dump()
		{
			System.Diagnostics.Debug.WriteLine(Path + "[" + GetHashCode() + "]", "DUMP");
			foreach(AbstractSchemaItem item in ChildItems)
			{
				item.Dump();
			}
		}
		
		public T FirstParentOfType<T>() where T : class
		{
			AbstractSchemaItem parent = ParentItem;
			for (int i = 0; i < 1000; i++)
			{
				if (parent is T typedParent) return typedParent;
				if (parent == null) return null;
				parent = parent.ParentItem;
			}
			return null;
		}
		
	}
}