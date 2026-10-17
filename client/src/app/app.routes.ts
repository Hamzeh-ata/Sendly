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
        path: 'research-runs',
        loadComponent: () =>
          import('./features/research-runs/research-runs.page').then((m) => m.ResearchRunsPage)
      },
      {
        path: 'research-runs/:id',
        loadComponent: () =>
          import('./features/research-runs/research-run-detail.page').then(
            (m) => m.ResearchRunDetailPage
          )
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
