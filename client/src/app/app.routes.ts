import { Routes } from '@angular/router';
import { Home } from './home';
import { Landing } from './landing';
import { Methodology } from './methodology';
import { Shell } from './shell';

export const routes: Routes = [
  {
    path: '',
    component: Shell,
    children: [
      { path: '', component: Landing },
      { path: 'odds', component: Home },
      { path: 'methodology', component: Methodology },
    ],
  },
];
