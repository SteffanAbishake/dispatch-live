package com.dispatch.driver;
import android.Manifest;
import android.app.*;
import android.content.*;
import android.content.pm.*;
import android.location.*;
import android.os.*;
import org.json.JSONObject;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicBoolean;

public class TrackingService extends Service implements LocationListener {
 public static final String START="dispatch.START",STOP="dispatch.STOP";
 public static volatile boolean running=false;
 private final Handler main=new Handler(Looper.getMainLooper());
 private final ScheduledExecutorService network=Executors.newSingleThreadScheduledExecutor();
 private final AtomicBoolean tracking=new AtomicBoolean(false);
 private volatile boolean creating=false;
 private volatile Location latest;
 private LocationManager locations;
 private Store store;
 private ScheduledFuture<?> loop;
 private long sequence=0,expiresAt=0;
 private static final int NOTIFICATION=71;
 @Override public IBinder onBind(Intent intent){return null;}
 @Override public void onCreate(){super.onCreate();store=new Store(this);locations=getSystemService(LocationManager.class);getSystemService(NotificationManager.class).createNotificationChannel(new NotificationChannel("tracking","Active delivery location",NotificationManager.IMPORTANCE_LOW));}
 private Notification notification(String text){PendingIntent open=PendingIntent.getActivity(this,0,new Intent(this,MainActivity.class),PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE);PendingIntent stop=PendingIntent.getService(this,1,new Intent(this,TrackingService.class).setAction(STOP),PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE);return new Notification.Builder(this,"tracking").setSmallIcon(R.drawable.ic_tracking).setContentTitle("Dispatch · Location sharing").setContentText(text).setContentIntent(open).setOngoing(true).setOnlyAlertOnce(true).addAction(new Notification.Action.Builder(null,"Stop sharing",stop).build()).build();}
 private void message(String text){store.put("message",text);if(running)getSystemService(NotificationManager.class).notify(NOTIFICATION,notification(text));}
 @Override public int onStartCommand(Intent intent,int flags,int startId){
  if(intent!=null&&STOP.equals(intent.getAction())){stopSharing();return START_NOT_STICKY;}
  if(intent==null||!START.equals(intent.getAction())||running)return START_NOT_STICKY;
  if(!store.get("active").isEmpty()||!store.get("pending").isEmpty()){store.put("message","End the previous session first.");stopSelf();return START_NOT_STICKY;}
  boolean fine=checkSelfPermission(Manifest.permission.ACCESS_FINE_LOCATION)==PackageManager.PERMISSION_GRANTED,coarse=checkSelfPermission(Manifest.permission.ACCESS_COARSE_LOCATION)==PackageManager.PERMISSION_GRANTED;
  if(!fine&&!coarse){store.put("message","Location permission is required.");stopSelf();return START_NOT_STICKY;}
  try{if(Build.VERSION.SDK_INT>=29)startForeground(NOTIFICATION,notification("Starting delivery…"),ServiceInfo.FOREGROUND_SERVICE_TYPE_LOCATION);else startForeground(NOTIFICATION,notification("Starting delivery…"));}catch(Exception e){store.put("message","Open the app and check location permissions before starting.");stopSelf();return START_NOT_STICKY;}
  running=true;tracking.set(true);creating=true;
  network.submit(()->{try{
   JSONObject body=new JSONObject().put("reference",store.get("reference")).put("consent",true).put("consentVersion","location-v1");JSONObject delivery=Api.call(store,"/api/driver/deliveries","POST",body);String id=delivery.getString("id");expiresAt=delivery.getLong("expiresAt");store.put("active",id);creating=false;
   if(!tracking.get()){StopJob.mark(this,id);main.post(this::finishService);return;}
   main.post(()->startGps(fine,coarse));loop=network.scheduleWithFixedDelay(this::sendLatest,0,10,TimeUnit.SECONDS);
  }catch(Exception e){creating=false;tracking.set(false);message(e.getMessage()+" Refresh your account to check for an open delivery.");main.post(this::finishService);}});
  return START_NOT_STICKY;
 }
 @SuppressWarnings("MissingPermission") private void startGps(boolean fine,boolean coarse){
  if(!tracking.get())return;
  try{boolean requested=false;if(fine&&locations.isProviderEnabled(LocationManager.GPS_PROVIDER)){locations.requestLocationUpdates(LocationManager.GPS_PROVIDER,10000,0,this,Looper.getMainLooper());requested=true;}if((fine||coarse)&&locations.isProviderEnabled(LocationManager.NETWORK_PROVIDER)){locations.requestLocationUpdates(LocationManager.NETWORK_PROVIDER,10000,0,this,Looper.getMainLooper());requested=true;}if(!requested){message("Enable phone Location, then start a new delivery.");stopSharing();}else message("Sharing location. You can switch apps or lock the screen.");}
  catch(SecurityException e){message("Location permission was removed.");stopSharing();}
 }
 private void sendLatest(){if(!tracking.get())return;PowerManager.WakeLock wake=getSystemService(PowerManager.class).newWakeLock(PowerManager.PARTIAL_WAKE_LOCK,"Dispatch:upload");wake.acquire(45000);
  try{
   String id=store.get("active");JSONObject me=Api.call(store,"/api/driver/me","GET",null);JSONObject remote=me.optJSONObject("delivery");if(!me.optBoolean("approved")||remote==null||!id.equals(remote.optString("id"))||System.currentTimeMillis()>=expiresAt){main.post(this::stopSharing);return;}
   Location fix=latest;if(fix==null){message("Waiting for a fresh GPS fix.");return;}long age=(SystemClock.elapsedRealtimeNanos()-fix.getElapsedRealtimeNanos())/1000000;
   if(age<0||age>30000){message("GPS unavailable. Dashboard shows your last update.");return;}
   // Never turn a cached position into a fresh update. Preserve its original sample age.
   JSONObject payload=new JSONObject().put("latitude",fix.getLatitude()).put("longitude",fix.getLongitude()).put("accuracy",fix.getAccuracy()).put("capturedAt",fix.getTime()).put("sequence",sequence++);
   if(tracking.get()){Api.call(store,"/api/driver/deliveries/"+id+"/location","PUT",payload);if(tracking.get())message("Location sent · accuracy ±"+(int)fix.getAccuracy()+" m");}
  }catch(Api.Failure e){if(e.status==401||e.status==403||e.status==409)main.post(this::stopSharing);else if(tracking.get())message("Server unavailable. Your last update is shown.");}
  catch(Exception e){if(tracking.get())message("Offline. Dashboard shows your last update.");}
  finally{if(wake.isHeld())wake.release();}
 }
 private void stopSharing(){tracking.set(false);latest=null;try{locations.removeUpdates(this);}catch(Exception ignored){}if(loop!=null)loop.cancel(false);String id=store.get("active");if(!id.isEmpty())StopJob.mark(this,id);else store.put("message","Sharing stopped.");if(!creating)finishService();}
 private void finishService(){running=false;stopForeground(STOP_FOREGROUND_REMOVE);stopSelf();}
 @Override public void onLocationChanged(Location location){if(tracking.get())latest=new Location(location);}
 @Override public void onProviderDisabled(String provider){latest=null;message("Location is disabled. Dashboard shows the last known position.");}
 @Override public void onProviderEnabled(String provider){}
 @Override public void onStatusChanged(String provider,int status,Bundle extras){}
 @Override public void onTaskRemoved(Intent rootIntent){super.onTaskRemoved(rootIntent);/* UI removal is not consent withdrawal; the ongoing notification remains. */}
 @Override public void onDestroy(){tracking.set(false);running=false;latest=null;try{locations.removeUpdates(this);}catch(Exception ignored){}network.shutdownNow();super.onDestroy();}
}
