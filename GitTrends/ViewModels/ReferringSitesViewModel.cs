﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using AsyncAwaitBestPractices.MVVM;
using GitTrends.Shared;
using Xamarin.Forms;

namespace GitTrends
{
    class ReferringSitesViewModel : BaseViewModel
    {
        readonly GitHubApiV3Service _gitHubApiV3Service;

        bool _isRefreshing;
        bool _isActivityIndicatorVisible;
        IReadOnlyList<MobileReferringSiteModel> _mobileReferringSiteList = Enumerable.Empty<MobileReferringSiteModel>().ToList();

        public ReferringSitesViewModel(GitHubApiV3Service gitHubApiV3Service, AnalyticsService analyticsService) : base(analyticsService)
        {
            _gitHubApiV3Service = gitHubApiV3Service;
            RefreshCommand = new AsyncCommand<(string Owner, string Repository)>(repo => ExecuteRefreshCommand(repo.Owner, repo.Repository));
        }

        public ICommand RefreshCommand { get; }

        public bool IsActivityIndicatorVisible
        {
            get => _isActivityIndicatorVisible;
            set => SetProperty(ref _isActivityIndicatorVisible, value);
        }

        public bool IsRefreshing
        {
            get => _isRefreshing;
            set => SetProperty(ref _isRefreshing, value);
        }

        public IReadOnlyList<MobileReferringSiteModel> MobileReferringSitesList
        {
            get => _mobileReferringSiteList;
            set => SetProperty(ref _mobileReferringSiteList, value);
        }

        async Task ExecuteRefreshCommand(string owner, string repository)
        {
            //Only show the Activity Indicator when the page is first loaded
            if (!MobileReferringSitesList.Any())
            {
                IsActivityIndicatorVisible = true;
            }

            try
            {
                var referringSitesList = await _gitHubApiV3Service.GetReferringSites(owner, repository).ConfigureAwait(false);
                var mobileReferringSitesList_NoFavIcon = referringSitesList.Select(x => new MobileReferringSiteModel(x, "DefaultProfileImage"));

                //Display the Referring Sites and hide the activity indicators while FavIcons are still being retreived
                IsActivityIndicatorVisible = false;
                displayMobileReferringSites(mobileReferringSitesList_NoFavIcon);

                var mobileReferringSitesList_WithFavIcon = await GetMobileReferringSiteWithFavIconList(referringSitesList).ConfigureAwait(false);

                //Display the Final Referring Sites with FavIcons
                displayMobileReferringSites(mobileReferringSitesList_WithFavIcon);
            }
            finally
            {
                IsActivityIndicatorVisible = IsRefreshing = false;
            }

            void displayMobileReferringSites(in IEnumerable<MobileReferringSiteModel> mobileReferringSiteList) => MobileReferringSitesList = mobileReferringSiteList.OrderByDescending(x => x.TotalCount).ThenByDescending(x => x.TotalUniqueCount).ToList();
        }

        async Task<List<MobileReferringSiteModel>> GetMobileReferringSiteWithFavIconList(List<ReferringSiteModel> referringSites)
        {
            var mobileReferringSiteList = new List<MobileReferringSiteModel>();

            var favIconTaskList = referringSites.Select(x => setFavIcon(x)).ToList();

            while (favIconTaskList.Any())
            {
                var completedFavIconTask = await Task.WhenAny(favIconTaskList).ConfigureAwait(false);
                favIconTaskList.Remove(completedFavIconTask);

                var mobileReferringSiteModel = await completedFavIconTask.ConfigureAwait(false);
                mobileReferringSiteList.Add(mobileReferringSiteModel);
            }

            return mobileReferringSiteList;

            static async Task<MobileReferringSiteModel> setFavIcon(ReferringSiteModel referringSiteModel)
            {
                if (referringSiteModel.ReferrerUri != null)
                {
                    var favIcon = await FavIconService.GetFavIconImageSource(referringSiteModel.ReferrerUri.ToString()).ConfigureAwait(false);
                    return new MobileReferringSiteModel(referringSiteModel, favIcon);
                }

                return new MobileReferringSiteModel(referringSiteModel, null);
            }
        }
    }
}
