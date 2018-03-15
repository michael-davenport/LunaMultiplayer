﻿using Contracts;
using LunaClient.Base;
using LunaClient.Systems.Lock;
using LunaClient.Systems.SettingsSys;
using LunaCommon.Enums;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;

namespace LunaClient.Systems.ShareProgress
{
    /// <summary>
    /// A system for synchronizing progress between the clients
    /// (funds, science, reputation, technology, contracts)
    /// </summary>
    class ShareProgressSystem : MessageSystem<ShareProgressSystem, ShareProgressMessageSender, ShareProgressMessageHandler>
    {
        public override string SystemName { get; } = nameof(ShareProgressSystem);

        private ShareProgressEvents ShareProgressEvents { get; } = new ShareProgressEvents();

        public bool IncomingFundsProcessing;
        public bool IncomingScienceProcessing;
        public bool IncomingReputationProcessing;
        public bool IncomingTechnologyProcessing;
        public bool IncomingContractsProcessing;

        private int defaultContractGenerateIterations;
        
        private double savedFunds;
        private float savedScience;
        private float savedReputation;

        #region UnityMethods
        protected override void OnEnabled()
        {
            base.OnEnabled();

            this.IncomingFundsProcessing = false;
            this.IncomingScienceProcessing = false;
            this.IncomingReputationProcessing = false;
            this.IncomingTechnologyProcessing = false;
            this.IncomingContractsProcessing = false;
            this.defaultContractGenerateIterations = ContractSystem.generateContractIterations;
            this.savedFunds = 0;
            this.savedScience = 0;
            this.savedReputation = 0;

            if (SettingsSystem.ServerSettings.GameMode != GameMode.Sandbox)
            {
                this.SubscribeToBasicEvents();

                if (SettingsSystem.ServerSettings.GameMode == GameMode.Career)
                {
                    this.TryGetContractLock();
                    SetupRoutine(new RoutineDefinition(10000, RoutineExecution.Update, this.TryGetContractLock));

                    this.SubscribeToContractEvents();
                }
            }
        }

        protected override void OnDisabled()
        {
            base.OnDisabled();

            if (SettingsSystem.ServerSettings.GameMode != GameMode.Sandbox)
            {
                UnsubscribeFromBasicEvents();

                if (SettingsSystem.ServerSettings.GameMode == GameMode.Career)
                {
                    UnsubscribeFromContractEvents();
                }
            }
        }
        #endregion

        #region PublicMethods
        /// <summary>
        /// Saves the current funds, science and reputation in memory.
        /// So they can be later applied again with RestoreBasicProgress.
        /// </summary>
        public void SaveBasicProgress()
        {
            if (SettingsSystem.ServerSettings.GameMode != GameMode.Sandbox)
            {
                this.savedScience = ResearchAndDevelopment.Instance.Science;

                if (SettingsSystem.ServerSettings.GameMode == GameMode.Career)
                {
                    this.savedFunds = Funding.Instance.Funds;
                    this.savedReputation = Reputation.Instance.reputation;
                }
            }
        }

        /// <summary>
        /// Restores the funds, science and repuation that was saved before.
        /// </summary>
        public void RestoreBasicProgress()
        {
            if (SettingsSystem.ServerSettings.GameMode != GameMode.Sandbox)
            {
                ResearchAndDevelopment.Instance.SetScience(this.savedScience, TransactionReasons.None);

                if (SettingsSystem.ServerSettings.GameMode == GameMode.Career)
                {
                    Funding.Instance.SetFunds(this.savedFunds, TransactionReasons.None);
                    Reputation.Instance.SetReputation(this.savedReputation, TransactionReasons.None);
                }
            }
        }
        #endregion

        #region PrivateMethods
        private void SubscribeToBasicEvents()
        {
            GameEvents.OnFundsChanged.Add(ShareProgressEvents.FundsChanged);
            GameEvents.OnReputationChanged.Add(ShareProgressEvents.ReputationChanged);
            GameEvents.OnScienceChanged.Add(ShareProgressEvents.ScienceChanged);
            GameEvents.OnTechnologyResearched.Add(ShareProgressEvents.TechnologyResearched);
        }

        private void UnsubscribeFromBasicEvents()
        {
            GameEvents.OnFundsChanged.Remove(ShareProgressEvents.FundsChanged);
            GameEvents.OnReputationChanged.Remove(ShareProgressEvents.ReputationChanged);
            GameEvents.OnScienceChanged.Remove(ShareProgressEvents.ScienceChanged);
            GameEvents.OnTechnologyResearched.Remove(ShareProgressEvents.TechnologyResearched);
        }

        private void SubscribeToContractEvents()
        {
            GameEvents.Contract.onAccepted.Add(ShareProgressEvents.ContractAccepted);
            GameEvents.Contract.onCancelled.Add(ShareProgressEvents.ContractCancelled);
            GameEvents.Contract.onCompleted.Add(ShareProgressEvents.ContractCompleted);
            GameEvents.Contract.onContractsListChanged.Add(ShareProgressEvents.ContractsListChanged);
            GameEvents.Contract.onContractsLoaded.Add(ShareProgressEvents.ContractsLoaded);
            GameEvents.Contract.onDeclined.Add(ShareProgressEvents.ContractDeclined);
            GameEvents.Contract.onFailed.Add(ShareProgressEvents.ContractFailed);
            GameEvents.Contract.onFinished.Add(ShareProgressEvents.ContractFinished);
            GameEvents.Contract.onOffered.Add(ShareProgressEvents.ContractOffered);
            GameEvents.Contract.onParameterChange.Add(ShareProgressEvents.ContractParameterChanged);
            GameEvents.Contract.onRead.Add(ShareProgressEvents.ContractRead);
            GameEvents.Contract.onSeen.Add(ShareProgressEvents.ContractSeen);
        }

        private void UnsubscribeFromContractEvents()
        {
            GameEvents.Contract.onAccepted.Remove(ShareProgressEvents.ContractAccepted);
            GameEvents.Contract.onCancelled.Remove(ShareProgressEvents.ContractCancelled);
            GameEvents.Contract.onCompleted.Remove(ShareProgressEvents.ContractCompleted);
            GameEvents.Contract.onContractsListChanged.Remove(ShareProgressEvents.ContractsListChanged);
            GameEvents.Contract.onContractsLoaded.Remove(ShareProgressEvents.ContractsLoaded);
            GameEvents.Contract.onDeclined.Remove(ShareProgressEvents.ContractDeclined);
            GameEvents.Contract.onFailed.Remove(ShareProgressEvents.ContractFailed);
            GameEvents.Contract.onFinished.Remove(ShareProgressEvents.ContractFinished);
            GameEvents.Contract.onOffered.Remove(ShareProgressEvents.ContractOffered);
            GameEvents.Contract.onParameterChange.Remove(ShareProgressEvents.ContractParameterChanged);
            GameEvents.Contract.onRead.Remove(ShareProgressEvents.ContractRead);
            GameEvents.Contract.onSeen.Remove(ShareProgressEvents.ContractSeen);
        }

        private void TryGetContractLock()
        {
            if (!LockSystem.LockQuery.ContractLockExists())
            {
                LockSystem.Singleton.AcquireContractLock();
            }

            //Update the ContractSystem generation depending on if the current player has the lock or not.
            if (!LockSystem.LockQuery.ContractLockBelongsToPlayer(SettingsSystem.CurrentSettings.PlayerName))
            {
                ContractSystem.generateContractIterations = 0;
                LunaLog.Log("You have no ContractLock and are not allowed to generate contracts.");
            }
            else
            {
                ContractSystem.generateContractIterations = this.defaultContractGenerateIterations;
                LunaLog.Log("You have the ContractLock and you will generate contracts.");
            }
        }
        #endregion
    }
}
