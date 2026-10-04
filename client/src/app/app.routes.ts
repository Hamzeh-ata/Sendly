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
        path: 'history',
        loadComponent: () => import('./features/history/history.page').then((m) => m.HistoryPage)
      },
      {
        path: 'history/:id',
        loadComponent: () =>
          import('./features/history-detail/history-detail.page').then((m) => m.HistoryDetailPage)
      },
      {
        path: 'settings',
        loadComponent: () => import('./features/settings/settings.page').then((m) => m.SettingsPage)
      }
    ]
  },
  { path: '**', redirectTo: 'compose' }
];
