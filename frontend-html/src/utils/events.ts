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

export type IEventHandlerSubscriber<T> = (arg?: T) => void;

export class EventHandler<T> {
  newId = 0;
  subscribers = new Map();

  subscribe(subscriber: IEventHandlerSubscriber<T>) {
    const myId = this.newId++;
    this.subscribers.set(myId, subscriber);
    return () => {
      this.subscribers.delete(myId);
    };
  }

  trigger(arg?: T) {
    for (let subscriber of this.subscribers.values()) {
      subscriber(arg);
    }
  }
}