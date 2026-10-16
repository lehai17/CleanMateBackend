import React,{createContext,useContext,useEffect,useMemo,useRef,useState} from 'react';
import * as SecureStore from 'expo-secure-store';
import {api,TOKEN_KEY} from '../api/axios';
import {meApi} from '../api/auth';
import {createSessionGuard,currentSessionExpired,createSerialQueue} from '../auth/session';

const USER_KEY='__USER_KEY__';
const Context=createContext(undefined);
export function AuthProvider({children}){
  const [user,setUser]=useState(null),[token,setToken]=useState(null),[loading,setLoading]=useState(true),[restoreError,setRestoreError]=useState('');
  const [logoutFailed,setLogoutFailed]=useState(false);
  const guard=useRef(createSessionGuard()).current,enqueue=useRef(createSerialQueue()).current,alive=useRef(true),userRef=useRef(null);
  const callbackToken=guard.snapshot().token;
  const accept=current=>{userRef.current=current;setUser(current);};
  const persist=(expected,action)=>enqueue(async()=>{if(guard.isCurrent(expected))await action();});
  const clearSession=async()=>{
    const expected=guard.replace(null);accept(null);setToken(null);setLoading(false);setRestoreError('');setLogoutFailed(false);
    try{await persist(expected,async()=>{await SecureStore.deleteItemAsync(TOKEN_KEY);await SecureStore.deleteItemAsync(USER_KEY);});}
    catch{if(alive.current&&guard.isCurrent(expected)){setLogoutFailed(true);setRestoreError('Không thể xóa phiên đã lưu. Vui lòng thử lại.');}}
  };
  const logout=async()=>{if(guard.snapshot().token===callbackToken)await clearSession();};
  const refreshUser=async current=>{
    if(guard.snapshot().token!==callbackToken)return;
    const expected=guard.snapshot();if(!expected.token||current?.id!==userRef.current?.id)return;
    await persist(expected,()=>SecureStore.setItemAsync(USER_KEY,JSON.stringify(current)));
    if(alive.current&&guard.isCurrent(expected))accept(current);
  };
  const login=async(current,accessToken)=>{
    if(guard.snapshot().token!==callbackToken)return;
    const expected=guard.replace(accessToken);accept(null);setToken(null);setRestoreError('');setLogoutFailed(false);
    try{
      await persist(expected,async()=>{await SecureStore.setItemAsync(TOKEN_KEY,accessToken);if(guard.isCurrent(expected))await SecureStore.setItemAsync(USER_KEY,JSON.stringify(current));});
      if(alive.current&&guard.isCurrent(expected)){accept(current);setToken(accessToken);setLoading(false);}
    }catch(error){if(guard.isCurrent(expected))await clearSession();throw error;}
  };
  const restore=async()=>{
    if(logoutFailed)return logout();
    const begin=guard.replace(guard.snapshot().token);setLoading(true);setRestoreError('');let expected=begin;
    try{
      const accessToken=await SecureStore.getItemAsync(TOKEN_KEY);if(!alive.current||!guard.isCurrent(begin))return;
      expected=guard.replace(accessToken);if(!accessToken){accept(null);setToken(null);return;}
      const current=await meApi();if(!alive.current||!guard.isCurrent(expected))return;
      if(!['Customer','Cleaner'].includes(current.role)){await clearSession();return;}
      await persist(expected,()=>SecureStore.setItemAsync(USER_KEY,JSON.stringify(current)));
      if(alive.current&&guard.isCurrent(expected)){accept(current);setToken(accessToken);}
    }catch(error){
      if(!alive.current||!guard.isCurrent(expected))return;
      if(error?.response?.status===401)await clearSession();
      else setRestoreError('Không thể kết nối để kiểm tra tài khoản. Vui lòng thử lại.');
    }finally{if(alive.current&&guard.isCurrent(expected))setLoading(false);}
  };
  useEffect(()=>{
    alive.current=true;
    const id=api.interceptors.response.use(response=>response,error=>{
      if(alive.current&&currentSessionExpired(error,guard.snapshot().token))void clearSession();
      return Promise.reject(error);
    });
    void restore();return()=>{alive.current=false;guard.replace(guard.snapshot().token);api.interceptors.response.eject(id);};

  },[]);
  const value=useMemo(()=>({user,token,login,logout,refreshUser,restore,loading,restoreError,logoutFailed}),[user,token,loading,restoreError,logoutFailed]);
  return <Context.Provider value={value}>{children}</Context.Provider>;
}
export function useAuth(){const value=useContext(Context);if(!value)throw new Error('useAuth must be used within AuthProvider');return value;}
