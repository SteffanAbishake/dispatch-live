"""Real HTTP + SQLite integration checks. Run after dotnet build backend -c Release."""
import os, pathlib, tempfile, subprocess, threading, time, json, urllib.request, urllib.error, http.cookiejar, re, sqlite3, secrets, hashlib, socket
root=pathlib.Path(__file__).resolve().parents[1]
dll=root/'backend/bin/Release/net10.0/Dispatch.Api.dll'
assert dll.exists(), 'Build the backend in Release first.'
with tempfile.TemporaryDirectory() as temp:
 with socket.socket() as sock:sock.bind(('127.0.0.1',0));port=sock.getsockname()[1]
 base=f'http://127.0.0.1:{port}';password=secrets.token_urlsafe(24);dbpath=str(pathlib.Path(temp)/'test.db');env=os.environ.copy()
 env.update({'ASPNETCORE_ENVIRONMENT':'Development','ASPNETCORE_URLS':base,'ConnectionStrings__Database':'Data Source='+dbpath,'Bootstrap__Email':'admin@example.test','Bootstrap__Password':password,'Otp__DevelopmentCodes':'true','Logging__LogLevel__Microsoft.EntityFrameworkCore':'Warning'})
 process=subprocess.Popen([os.environ.get('DOTNET','dotnet'),str(dll)],cwd=temp,env=env,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True)
 lines=[]
 def read_log():
  for line in process.stdout:lines.append(line)
 threading.Thread(target=read_log,daemon=True).start()
 admin=urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
 def call(path,method='GET',data=None,token=None,client=None,expected=200,protected=True):
  headers={'Content-Type':'application/json'}
  if protected:headers['X-Dispatch-Request']='1'
  if token:headers['Authorization']='Bearer '+token
  req=urllib.request.Request(base+path,data=None if data is None else json.dumps(data).encode(),method=method,headers=headers)
  try:
   response=client.open(req,timeout=10) if client else urllib.request.urlopen(req,timeout=10)
   status=response.status;text=response.read().decode()
  except urllib.error.HTTPError as e:status=e.code;text=e.read().decode()
  assert status==expected,(path,status,expected,text[:500])
  return json.loads(text) if text else None
 try:
  for _ in range(100):
   try:call('/health');break
   except Exception:
    if process.poll() is not None:raise RuntimeError('Backend startup failed. '+''.join(lines[-8:]))
    time.sleep(.1)
  call('/api/admin/drivers',expected=401)
  call('/api/auth/admin','POST',{'email':'admin@example.test','password':password},client=admin,protected=False,expected=403)
  call('/api/auth/admin','POST',{'email':'admin@example.test','password':password},client=admin)
  ids=[];tokens=[]
  for i in range(2):
   phone=f'+9477000000{i}'
   ids.append(call('/api/admin/drivers','POST',{'name':f'Driver {i+1}','phone':phone},client=admin)['id'])
   call('/api/auth/otp/request','POST',{'phone':phone})
   for _ in range(50):
    matches=re.findall(re.escape(phone)+r': (\d{6})',''.join(lines))
    if matches:break
    time.sleep(.05)
   assert matches,'Development OTP not logged'
   tokens.append(call('/api/auth/otp/verify','POST',{'phone':phone,'code':matches[-1]})['token'])
   call('/api/auth/otp/verify','POST',{'phone':phone,'code':matches[-1]},expected=401)
  call('/api/admin/drivers',token=tokens[0],expected=403)
  start={'reference':'TEST-1','consent':True,'consentVersion':'location-v1'}
  call('/api/driver/deliveries','POST',start,token=tokens[0],expected=403)
  for id in ids:call('/api/admin/drivers/'+id,'PATCH',{'approved':True},client=admin)
  call('/api/driver/deliveries','POST',{**start,'consent':False},token=tokens[0],expected=400)
  deliveries=[call('/api/driver/deliveries','POST',start,token=t)['id'] for t in tokens]
  call('/api/driver/deliveries','POST',start,token=tokens[0],expected=409)
  fix={'latitude':6.91,'longitude':79.86,'accuracy':12,'capturedAt':int(time.time()*1000),'sequence':1}
  path=lambda id:'/api/driver/deliveries/'+id
  call(path(deliveries[0])+'/location','PUT',{**fix,'latitude':100},token=tokens[0],expected=400)
  call(path(deliveries[0])+'/location','PUT',{**fix,'capturedAt':1},token=tokens[0],expected=400)
  call(path(deliveries[1])+'/location','PUT',fix,token=tokens[0],expected=409)
  call(path(deliveries[1])+'/end','POST',{},token=tokens[0],expected=404)
  for t,d in zip(tokens,deliveries):call(path(d)+'/location','PUT',fix,token=t)
  call(path(deliveries[0])+'/location','PUT',fix,token=tokens[0],expected=409)
  rows=call('/api/admin/deliveries',client=admin);assert len(rows)==2 and all(r['status']=='Live' for r in rows)
  with sqlite3.connect(dbpath) as db:
   old=int(time.time()*1000)-60000;db.execute('UPDATE Deliveries SET CapturedAt=?, ReceivedAt=? WHERE Id=?',(old,old,deliveries[0]))
   other=secrets.token_hex(32)
   db.execute('INSERT INTO Users (Id,CompanyId,Name,Role,Phone,Email,PasswordHash,Approved,Enabled) VALUES (?,?,?,?,?,?,?,?,?)',('other-admin','company-b','Other','Admin',None,'other@example.test',None,1,1))
   db.execute('INSERT INTO Sessions (Id,UserId,TokenHash,ExpiresAt) VALUES (?,?,?,?)',('other-session','other-admin',hashlib.sha256(other.encode()).hexdigest().upper(),int(time.time()*1000)+100000))
  assert call('/api/admin/deliveries',token=other)==[]
  call('/api/admin/drivers/'+ids[0],'PATCH',{'approved':False},token=other,expected=404)
  call('/api/admin/deliveries/'+deliveries[0]+'/end','POST',{},token=other,expected=404)
  assert next(r for r in call('/api/admin/deliveries',client=admin) if r['id']==deliveries[0])['status']=='Location unavailable'
  call(path(deliveries[0])+'/end','POST',{},token=tokens[0]);call(path(deliveries[0])+'/end','POST',{},token=tokens[0])
  call(path(deliveries[0])+'/location','PUT',{**fix,'sequence':2},token=tokens[0],expected=409)
  row=next(r for r in call('/api/admin/deliveries',client=admin) if r['id']==deliveries[0]);assert row['latitude'] is None and row['longitude'] is None and row['status']=='Ended'
  call('/api/admin/drivers/'+ids[1],'PATCH',{'approved':False},client=admin)
  call(path(deliveries[1])+'/location','PUT',{**fix,'sequence':2},token=tokens[1],expected=403)
  assert all(not r['active'] and r['latitude'] is None for r in call('/api/admin/deliveries',client=admin))
  call('/api/auth/logout','POST',{},token=tokens[0]);call('/api/driver/me',token=tokens[0],expected=401)
  print('PASS: OTP single-use, approvals, two drivers, roles, company isolation, consent, GPS validation, stale detection, ordering, stop, clearing, suspension, logout.')
 finally:
  process.terminate()
  try:process.wait(timeout=5)
  except subprocess.TimeoutExpired:process.kill();process.wait()
