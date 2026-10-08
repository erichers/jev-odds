import { Component, inject } from '@angular/core';
import { Title } from '@angular/platform-browser';

@Component({
  selector: 'app-methodology',
  templateUrl: './methodology.html',
})
export class Methodology {
  constructor() {
    inject(Title).setTitle('Methodology · Jev Odds');
  }
}
