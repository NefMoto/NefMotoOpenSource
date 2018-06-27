/*
Nefarious Motorsports ME7 ECU Flasher
Copyright (C) 2017  Nefarious Motorsports Inc

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.

Contact by Email: tony@nefariousmotorsports.com
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;

namespace Shared
{
    public abstract class Operation
    {
        public delegate void PercentCompleteDelegate(float percentComplete);
        public delegate void CompletedOperationDelegate(Operation operation, bool success);

        public event CompletedOperationDelegate CompletedOperationEvent;
        public event PercentCompleteDelegate PercentCompletedEvent;

        public Operation()
        {   
            IsRunning = false;         
        }

        public void Start()
        {
			bool canStart = CanOperationStart();

			IsRunning = true;//need to set this early in case the first action started calls action completed

            if (canStart)
            {
                mOperationStartTime = DateTime.Now;
                mOperationEndTime = mOperationStartTime;

                OnOperationStart();
            }
            else
            {
                OperationCompleted(false);
            }
        }

        protected virtual void OnOperationStart()
        {

        }

        public virtual void Abort()
        {
            if (IsRunning)
            {
                OperationCompleted(false);                
            }
        }

        protected virtual bool CanOperationStart()
        {
            return !IsRunning;
        }

        protected void OperationCompleted(bool success)
        {
			if (IsRunning)
			{
				mOperationEndTime = DateTime.Now;
				IsRunning = false;//must set to false before calling OnOperationCompleted to prevent re-entry

				success = OnOperationCompleted(success);

				if (CompletedOperationEvent != null)
				{
					CompletedOperationEvent(this, success);
				}
			}
        }

        protected virtual bool OnOperationCompleted(bool success)
        {
            return success;
        }

        protected void OnUpdatePercentComplete(float percentComplete)
        {
            if (PercentCompletedEvent != null)
            {
                PercentCompletedEvent(percentComplete);
            }
        }

        public bool IsRunning { get; private set; }

        public TimeSpan OperationElapsedTime
        {
            get
            {
                if (IsRunning)
                {
                    return DateTime.Now - mOperationStartTime;
                }
                else
                {
                    return mOperationEndTime - mOperationStartTime;
                }
            }
        }
        
        private DateTime mOperationStartTime;
        private DateTime mOperationEndTime;
    };
}
