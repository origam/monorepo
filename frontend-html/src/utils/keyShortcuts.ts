/*
Copyright 2005 - 2023 Advantage Solutions, s. r. o.

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


export function isSaveShortcut(event: any) {
  return event.key === "s" && (event.ctrlKey || event.metaKey);
}

export function isRefreshShortcut(event: any) {
  return event.key === "r" && (event.ctrlKey || event.metaKey);
}

export function isAddRecordShortcut(event: any) {
  return (
    ((event.ctrlKey || event.metaKey) && !event.shiftKey && event.key === "i") ||
    ((event.ctrlKey || event.metaKey) && event.shiftKey && event.key === "j") ||
    event.key === "Insert"
  );
}

export function isDeleteRecordShortcut(event: any) {
  return (event.ctrlKey || event.metaKey) && !event.shiftKey && event.key === "Delete";
}

export function isDuplicateRecordShortcut(event: any) {
  return (
    (event.ctrlKey || event.metaKey) && !event.shiftKey && (event.key === "d" || event.key === "k")
  );
}

export function isFilterRecordShortcut(event: any) {
  return (event.ctrlKey || event.metaKey) && event.key === "f";
}