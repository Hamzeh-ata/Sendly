import { Routes } from '@angular/router';
import { ShellComponent } from './layout/shell/shell.component';

export const routes: Routes = [
  {
    path: '',
    component: ShellComponent,
    children: [
      { path: '', redirectTo: 'compose', pathMatch: 'full' },
      {
        path: 'compose',
        loadComponent: () => import('./features/compose/compose.page').then((m) => m.ComposePage)
      },
      {
        path: 'templates',
        loadComponent: () => import('./features/templates/templates.page').then((m) => m.TemplatesPage)
      },
      {
        path: 'templates/new',
        loadComponent: () =>
          import('./features/templates/template-editor.page').then((m) => m.TemplateEditorPage)
      },
      {
        path: 'templates/:id/edit',
        loadComponent: () =>
          import('./features/templates/template-editor.page').then((m) => m.TemplateEditorPage)
      },
      {
        path: 'templates/:id/history',
        loadComponent: () =>
          import('./features/templates/template-history.page').then((m) => m.TemplateHistoryPage)
      },
      {
        path: 'projects',
        loadComponent: () => import('./features/projects/projects.page').then((m) => m.ProjectsPage)
      },
      {
        path: 'projects/new',
        loadComponent: () =>
          import('./features/projects/project-editor.page').then((m) => m.ProjectEditorPage)
      },
      {
        path: 'projects/:id',
        loadComponent: () =>
          import('./features/projects/project-detail.page').then((m) => m.ProjectDetailPage)
      },
      {
        path: 'projects/:id/edit',
        loadComponent: () =>
          import('./features/projects/project-editor.page').then((m) => m.ProjectEditorPage)
      },
      {
        path: 'researcher',
        loadComponent: () =>
          import('./features/researcher/researcher-shell.component').then(
            (m) => m.ResearcherShellComponent
          ),
        children: [
          {
            path: '',
            loadComponent: () =>
              import('./features/researcher/overview/researcher-overview.page').then(
                (m) => m.ResearcherOverviewPage
              )
          },
          {
            path: 'runs',
            loadComponent: () =>
              import('./features/research-runs/research-runs.page').then((m) => m.ResearchRunsPage)
          },
          {
            path: 'live',
            loadComponent: () =>
              import('./features/researcher/live/researcher-live-entry.page').then(
                (m) => m.ResearcherLiveEntryPage
              )
          },
          {
            path: 'live/:id',
            loadComponent: () =>
              import('./features/researcher/live/researcher-live.page').then(
                (m) => m.ResearcherLivePage
              )
          },
          { path: 'companies', redirectTo: '/prospects', pathMatch: 'full' },
          {
            path: 'sources',
            loadComponent: () =>
              import('./features/researcher/sources/researcher-sources.page').then(
                (m) => m.ResearcherSourcesPage
              )
          },
          {
            path: 'fetch',
            loadComponent: () =>
              import('./features/researcher/fetch/researcher-fetch.page').then(
                (m) => m.ResearcherFetchPage
              )
          },
          {
            path: 'fetch/:runId/:pageId',
            loadComponent: () =>
              import('./features/researcher/fetch/researcher-fetch-detail.page').then(
                (m) => m.ResearcherFetchDetailPage
              )
          },
          {
            path: 'errors',
            loadComponent: () =>
              import('./features/researcher/errors/researcher-errors.page').then(
                (m) => m.ResearcherErrorsPage
              )
          },
          {
            path: 'coverage',
            loadComponent: () =>
              import('./features/researcher/coverage/researcher-coverage.page').then(
                (m) => m.ResearcherCoveragePage
              )
          },
          {
            path: 'map',
            loadComponent: () =>
              import('./features/researcher/map/researcher-map.page').then(
                (m) => m.ResearcherMapPage
              )
          }
        ]
      },
      {
        path: 'research-runs',
        loadComponent: () =>
          import('./features/research-runs/research-runs.page').then((m) => m.ResearchRunsPage)
      },
      {
        path: 'research-runs/:id',
        redirectTo: 'researcher/live/:id',
        pathMatch: 'full'
      },
      {
        path: 'prospects',
        loadComponent: () => import('./features/prospects/prospects.page').then((m) => m.ProspectsPage)
      },
      {
        path: 'prospects/:id',
        loadComponent: () =>
          import('./features/prospects/prospect-detail.page').then((m) => m.ProspectDetailPage)
      },
      {
        path: 'sequences',
        loadComponent: () =>
          import('./features/sequences/sequences.page').then((m) => m.SequencesPage)
      },
      {
        path: 'sequences/new',
        loadComponent: () =>
          import('./features/sequences/sequence-editor.page').then((m) => m.SequenceEditorPage)
      },
      {
        path: 'sequences/:id',
        loadComponent: () =>
          import('./features/sequences/sequence-detail.page').then((m) => m.SequenceDetailPage)
      },
      {
        path: 'sequences/:id/edit',
        loadComponent: () =>
          import('./features/sequences/sequence-editor.page').then((m) => m.SequenceEditorPage)
      },
      {
        path: 'history',
        loadComponent: () => import('./features/history/history.page').then((m) => m.HistoryPage)
      },
      {
        path: 'history/:id',
        loadComponent: () =>
          import('./features/history-detail/history-detail.page').then((m) => m.HistoryDetailPage)
      },
      {
        path: 'signatures',
        loadComponent: () =>
          import('./features/signatures/signatures-redirect.page').then(
            (m) => m.SignaturesRedirectPage
          )
      },
      {
        path: 'settings',
        loadComponent: () => import('./features/settings/settings.page').then((m) => m.SettingsPage)
      }
    ]
  },
  { path: '**', redirectTo: 'compose' }
];
