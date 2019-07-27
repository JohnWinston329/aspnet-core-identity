import { Injectable } from '@angular/core';

@Injectable()
export class StateService {
    userState: UserState = { username: '', isAuthenticated: false, authenticationMethod: '' };
    notification: Notification = { message: '', type: '' };

    constructor() { }

    /**
     * setAuthentication
     */
    public setAuthentication(state: UserState) {
        this.userState = state;
    }

    public isAuthenticated() {
        return this.userState.isAuthenticated;
    }

    public username() {
        return this.userState.username;
    }

    public authenticationMethod() {
        return this.userState.authenticationMethod;
    }

    public displayNotification(notify: Notification) {
        this.notification.message = notify.message;
        this.notification.type = notify.type;

        setTimeout(() => {
            this.notification.message = '';
            this.notification.type = '';
        }, 8000);
    }

    public getNotification() {
        return this.notification;
    }
}

export interface UserState {
    username: string;
    isAuthenticated: boolean;
    authenticationMethod: string;
}

export interface Notification {
    message: string;
    type: string;
}