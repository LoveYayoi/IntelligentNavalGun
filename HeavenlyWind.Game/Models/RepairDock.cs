﻿using Sakuno.KanColle.Amatsukaze.Game.Models.Raw;
using System;

namespace Sakuno.KanColle.Amatsukaze.Game.Models
{
    public enum RepairDockState { Locked = -1, Idle, Repairing }

    public class RepairDock : CountdownModelBase, IID
    {
        public int ID { get; }

        RepairDockState r_State;
        public RepairDockState State
        {
            get { return r_State; }
            private set
            {
                if (r_State != value)
                {
                    r_State = value;
                    OnPropertyChanged(nameof(State));
                }
            }
        }

        Ship r_Ship;
        public Ship Ship
        {
            get { return r_Ship; }
            private set
            {
                if (r_Ship != value)
                {
                    r_Ship = value;
                    OnPropertyChanged(nameof(Ship));
                }
            }
        }

        internal bool PendingToUpdateMaterials { get; set; }

        public override TimeSpan RemainingTimeToNotify => TimeSpan.FromMinutes(1.0);

        public event Action<string> RepairCompleted = delegate { };

        internal RepairDock(RawRepairDock rpRawData)
        {
            ID = rpRawData.ID;

            Update(rpRawData);
        }

        internal void Update(RawRepairDock rpRawData)
        {
            State = rpRawData.State;

            if (State == RepairDockState.Repairing)
            {
                Ship = KanColleGame.Current.Port.Ships[rpRawData.ShipID];

                if ((Ship.State & ShipState.RepairingInAnchorage) == ShipState.RepairingInAnchorage)
                    Ship.OwnerFleet.AnchorageRepair.RemoveShipIfExists(Ship);

                Ship.OwnerRepairDock = this;
                Ship.State |= ShipState.Repairing;
                Ship.OwnerFleet?.Update();

                TimeToComplete = DateTimeUtil.UnixEpoch.AddMilliseconds(rpRawData.TimeToComplete);

                if (PendingToUpdateMaterials)
                {
                    var rMaterials = KanColleGame.Current.Port.Materials;

                    rMaterials.Fuel -= rpRawData.FuelConsumption;
                    rMaterials.Bullet -= rpRawData.BulletConsumption;
                    rMaterials.Steel -= rpRawData.SteelConsumption;
                    rMaterials.Bauxite -= rpRawData.BauxiteConsumption;

                    PendingToUpdateMaterials = false;
                }
            }
            else
            {
                if (Ship != null)
                {
                    Ship.OwnerRepairDock = null;
                    Ship.Repair(true);
                    Ship = null;
                }
                TimeToComplete = null;
            }
        }

        internal void CompleteRepair()
        {
            Ship.Repair(true);
            Ship = null;

            IsNotificated = true;
            State = RepairDockState.Idle;
            TimeToComplete = null;
        }

        protected override void TimeOut() => RepairCompleted(Ship.Info.TranslatedName);

        public override string ToString()
        {
            var rBuilder = StringBuilderCache.Acquire();
            rBuilder.Append("ID = ").Append(ID).Append(", State = ").Append(State);

            if (State == RepairDockState.Repairing)
                rBuilder.Append(", Ship = \"").Append(Ship.Info.TranslatedName).Append('\"').Append(", TimeToComplete = \"").Append(TimeToComplete.Value).Append('\"'); ;

            return rBuilder.GetStringAndRelease();
        }
    }
}
