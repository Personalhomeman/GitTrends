﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AsyncAwaitBestPractices;
using Autofac;
using GitTrends.Mobile.Shared;
using GitTrends.Shared;
using Xamarin.Essentials;
using Xamarin.Forms;

namespace GitTrends
{
    public class RepositoryPage : BaseContentPage<RepositoryViewModel>, ISearchPage
    {
        readonly WeakEventManager<string> _searchTextChangedEventManager = new WeakEventManager<string>();
        readonly GitHubAuthenticationService _gitHubAuthenticationService;

        public RepositoryPage(RepositoryViewModel repositoryViewModel,
                                GitHubAuthenticationService gitHubAuthenticationService,
                                AnalyticsService analyticsService) : base(PageTitles.RepositoryPage, repositoryViewModel, analyticsService)
        {
            _gitHubAuthenticationService = gitHubAuthenticationService;

            ViewModel.PullToRefreshFailed += HandlePullToRefreshFailed;
            SearchBarTextChanged += HandleSearchBarTextChanged;

            var collectionView = new CollectionView
            {
                ItemTemplate = new RepositoryDataTemplate(),
                BackgroundColor = Color.Transparent,
                SelectionMode = SelectionMode.Single,
                AutomationId = RepositoryPageAutomationIds.CollectionView
            };
            collectionView.SelectionChanged += HandleCollectionViewSelectionChanged;
            collectionView.SetBinding(CollectionView.ItemsSourceProperty, nameof(RepositoryViewModel.VisibleRepositoryList));

            var repositoriesListRefreshView = new RefreshView
            {
                AutomationId = RepositoryPageAutomationIds.RefreshView,
                Content = collectionView
            };
            repositoriesListRefreshView.SetDynamicResource(RefreshView.RefreshColorProperty, nameof(BaseTheme.RefreshControlColor));
            repositoriesListRefreshView.SetBinding(RefreshView.IsRefreshingProperty, nameof(RepositoryViewModel.IsRefreshing));
            repositoriesListRefreshView.SetBinding(RefreshView.CommandProperty, nameof(RepositoryViewModel.PullToRefreshCommand));

            var settingsToolbarItem = new ToolbarItem
            {
                Text = "Settings",
                Order = Device.RuntimePlatform is Device.Android ? ToolbarItemOrder.Secondary : ToolbarItemOrder.Default,
                AutomationId = RepositoryPageAutomationIds.SettingsButton
            };
            settingsToolbarItem.Clicked += HandleSettingsToolbarItem;
            ToolbarItems.Add(settingsToolbarItem);

            Content = repositoriesListRefreshView;
        }

        public event EventHandler<string> SearchBarTextChanged
        {
            add => _searchTextChangedEventManager.AddEventHandler(value);
            remove => _searchTextChangedEventManager.RemoveEventHandler(value);
        }

        public void OnSearchBarTextChanged(in string text) => _searchTextChangedEventManager.HandleEvent(this, text, nameof(SearchBarTextChanged));

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            if (Content is RefreshView refreshView
                        && refreshView.Content is CollectionView collectionView
                        && IsNullOrEmpty(collectionView.ItemsSource))
            {
                var token = await GitHubAuthenticationService.GetGitHubToken();

                if (GitHubAuthenticationService.Alias != DemoDataConstants.Alias
                    && (string.IsNullOrWhiteSpace(token.AccessToken) || string.IsNullOrWhiteSpace(GitHubAuthenticationService.Alias)))
                {
                    var shouldNavigateToSettingsPage = await DisplayAlert(GitHubUserNotFoundConstants.Title, GitHubUserNotFoundConstants.Description, GitHubUserNotFoundConstants.Accept, GitHubUserNotFoundConstants.Decline);

                    if (shouldNavigateToSettingsPage)
                        await NavigateToSettingsPage();
                }
                else
                {
                    refreshView.IsRefreshing = true;
                }
            }

            static bool IsNullOrEmpty(in IEnumerable? enumerable) => !enumerable?.GetEnumerator().MoveNext() ?? true;
        }

        async void HandleCollectionViewSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var collectionView = (CollectionView)sender;
            collectionView.SelectedItem = null;

            if (e?.CurrentSelection.FirstOrDefault() is Repository repository)
            {
                AnalyticsService.Track("Repository Tapped", new Dictionary<string, string>
                {
                    { nameof(Repository.OwnerLogin), repository.OwnerLogin },
                    { nameof(Repository.Name), repository.Name }
                });

                await NavigateToTrendsPage(repository);
            }
        }

        Task NavigateToSettingsPage()
        {
            using var scope = ContainerService.Container.BeginLifetimeScope();

            var profilePage = scope.Resolve<SettingsPage>();
            return MainThread.InvokeOnMainThreadAsync(() => Navigation.PushAsync(profilePage));
        }

        Task NavigateToTrendsPage(Repository repository)
        {
            using var scope = ContainerService.Container.BeginLifetimeScope();

            var trendsPage = scope.Resolve<TrendsPage>(new TypedParameter(typeof(Repository), repository));
            return MainThread.InvokeOnMainThreadAsync(() => Navigation.PushAsync(trendsPage));
        }

        async void HandleSettingsToolbarItem(object sender, EventArgs e)
        {
            AnalyticsService.Track("Settings Button Tapped");

            await NavigateToSettingsPage();
        }

        void HandlePullToRefreshFailed(object sender, PullToRefreshFailedEventArgs e)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                if (!Application.Current.MainPage.Navigation.ModalStack.Any()
                    && Application.Current.MainPage.Navigation.NavigationStack.Last() is RepositoryPage)
                {
                    await DisplayAlert(e.ErrorTitle, e.ErrorMessage, "OK");
                }
            });
        }

        void HandleSearchBarTextChanged(object sender, string searchBarText) => ViewModel.FilterRepositoriesCommand.Execute(searchBarText);
    }
}
