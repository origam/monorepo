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

#region license
/*
Copyright 2005 - 2021 Advantage Solutions, s. r. o.

This file is part of ORIGAM.

ORIGAM is free software: you can redistribute it and/or modify
it under the terms of the GNU Affero General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

ORIGAM is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.See the
GNU Affero General Public License for more details.

You should have received a copy of the GNU Affero General Public License
along with ORIGAM.  If not, see<http://www.gnu.org/licenses/>.
*/
#endregion

using System;
using System.Collections.Generic;

namespace Origam.Server
{
    public class PortalResult
    {
        private string _menu;
        private HelpTooltip _tooltip;
        private string _favorites;
        private IList<PortalResultSession> _sessions = new List<PortalResultSession>();
        private int _workQueueListRefreshInterval;
        private int _notificationBoxRefreshInterval;
        private string _slogan;
        private string _helpUrl;
        private PortalStyle _style = new PortalStyle();
        private int _maxRequestLength;

        public PortalResult(string menu)
        {
            _menu = menu;
        }

        public string Menu
        {
            get { return _menu; }
            set { _menu = value; }
        }

        public string Favorites
        {
            get { return _favorites; }
            set { _favorites = value; }
        }

        public HelpTooltip Tooltip
        {
            get { return _tooltip; }
            set { _tooltip = value; }
        }
	
        public IList<PortalResultSession> Sessions
        {
            get { return _sessions; }
        }

        private string _userName;

        public string UserName
        {
            get { return _userName; }
            set { _userName = value; }
        }

        public Guid UserId { private get; set; }
        public string AvatarLink => "internalApi/Avatar/" + UserId;

        public int WorkQueueListRefreshInterval
        {
            get { return _workQueueListRefreshInterval; }
            set { _workQueueListRefreshInterval = value; }
        }

        public int NotificationBoxRefreshInterval
        {
            get { return _notificationBoxRefreshInterval; }
            set { _notificationBoxRefreshInterval = value; }
        }

        public string Slogan
        {
            get { return _slogan; }
            set { _slogan = value; }
        }

        public string HelpUrl
        {
            get { return _helpUrl; }
            set { _helpUrl = value; }
        }

        public PortalStyle Style
        {
            get
            {
                return _style;
            }
        }

        public int MaxRequestLength
        {
            get { return _maxRequestLength; }
            set { _maxRequestLength = value; }
        }

        public string LogoUrl { get; set; }
        public string CustomAssetsRoute { get; set; }

        public int ChatRefreshInterval { get; set; }
        public string Title { get; set; }
        public bool ShowToolTipsForMemoFieldsOnly { get; set; }
        public IFilteringConfig FilteringConfig { get; set; }

        public string InitialScreenId { get; set; }
        public int RowStatesDebouncingDelayMilliseconds { get; set; }
    }
}
