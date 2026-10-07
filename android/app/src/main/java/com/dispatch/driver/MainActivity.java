package com.dispatch.driver;
import android.Manifest;
import android.app.*;
import android.content.*;
import android.content.pm.PackageManager;
import android.graphics.Color;
import android.location.LocationManager;
import android.os.*;
import android.provider.Settings;
import android.text.InputType;
import android.widget.*;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicBoolean;
import org.json.JSONObject;

public class MainActivity extends Activity {
 private Store store;private LinearLayout root;private TextView status;private EditText reference;private CheckBox consent;private Button startButton,stopButton;
 private volatile boolean approved=false;private final Handler main=new Handler(Looper.getMainLooper());private final ExecutorService network=Executors.newSingleThreadExecutor();private final AtomicBoolean busy=new AtomicBoolean(false);
 private final Runnable tick=new Runnable(){public void run(){if(status!=null){status.setText(store.get("message"));if(startButton!=null)startButton.setEnabled(approved&&!busy.get()&&!TrackingService.running&&store.get("active").isEmpty()&&store.get("pending").isEmpty());if(stopButton!=null)stopButton.setEnabled(TrackingService.running||!store.get("active").isEmpty()||!store.get("pending").isEmpty());}main.postDelayed(this,1000);}};
 interface Task{void run()throws Exception;}
 @Override public void onCreate(Bundle saved){super.onCreate(saved);store=new Store(this);getWindow().setStatusBarColor(Color.rgb(24,45,59));if(store.get("message").isEmpty())store.put("message","Not sharing your location.");render();}
 @Override public void onResume(){super.onResume();main.post(tick);if(!store.token().isEmpty()){if(!store.get("pending").isEmpty())StopJob.enqueue(this);refresh();}}
 @Override public void onPause(){main.removeCallbacks(tick);super.onPause();}
 @Override public void onDestroy(){main.removeCallbacks(tick);network.shutdownNow();super.onDestroy();}
 private TextView text(String value,int size){TextView view=new TextView(this);view.setText(value);view.setTextSize(size);view.setTextColor(Color.rgb(31,53,65));view.setPadding(0,12,0,12);root.addView(view);return view;}
 private EditText input(String hint,String value,int type){EditText view=new EditText(this);view.setHint(hint);view.setText(value);view.setInputType(type);view.setSingleLine(true);view.setPadding(12,18,12,18);root.addView(view);return view;}
 private Button button(String value,Runnable action){Button button=new Button(this);button.setText(value);button.setAllCaps(false);button.setOnClickListener(v->action.run());LinearLayout.LayoutParams params=new LinearLayout.LayoutParams(-1,-2);params.topMargin=14;root.addView(button,params);return button;}
 private void task(Task action){if(!busy.compareAndSet(false,true))return;network.submit(()->{try{action.run();}catch(Exception e){store.put("message",e.getMessage()==null?"Request failed.":e.getMessage());}finally{busy.set(false);}});}
 private String value(EditText edit){return edit.getText().toString().trim();}
 private void render(){startButton=null;stopButton=null;root=new LinearLayout(this);root.setOrientation(LinearLayout.VERTICAL);root.setPadding(28,28,28,35);root.setBackgroundColor(Color.rgb(245,248,247));ScrollView scroll=new ScrollView(this);scroll.addView(root);setContentView(scroll);text("DISPATCH / DRIVER",14);text("Your delivery.\nYour location. Your control.",28);status=text(store.get("message"),16);
  if(store.token().isEmpty()){
   text("Sign in with the phone number added by your dispatcher. Each driver uses their own number.",16);
   EditText base=input("HTTPS server address",store.get("base"),InputType.TYPE_CLASS_TEXT|InputType.TYPE_TEXT_VARIATION_URI),phone=input("Phone number (+9477…)",store.get("phone"),InputType.TYPE_CLASS_PHONE),code=input("Six-digit verification code","",InputType.TYPE_CLASS_NUMBER);
   button("Send code",()->{String b=value(base).replaceAll("/+$",""),p=value(phone);task(()->{checkAccount(b,p);store.put("base",b);JSONObject result=Api.call(store,"/api/auth/otp/request","POST",new JSONObject().put("phone",p));store.put("message",result.getString("message"));});});
   button("Verify & sign in",()->{String b=value(base).replaceAll("/+$",""),p=value(phone),c=value(code);task(()->{checkAccount(b,p);store.put("base",b);JSONObject result=Api.call(store,"/api/auth/otp/verify","POST",new JSONObject().put("phone",p).put("code",c));store.token(result.getString("token"));store.put("phone",p);approved=result.getBoolean("approved");store.put("message",approved?"Signed in. Ready to start.":"Waiting for dispatcher approval.");if(!store.get("pending").isEmpty())StopJob.enqueue(this);main.post(()->{render();refresh();});});});
   if(BuildConfig.DEBUG)text("Development mode: the server can print test codes instead of sending SMS. Ask your developer for the code from the server terminal.",13);
  }else{
   text("Your delivery company can see your latest position while sharing is on. Sharing continues when you lock the screen or use another app. An Android notification stays visible.",16);
   reference=input("Delivery reference (e.g. DLV-1001)",store.get("reference"),InputType.TYPE_CLASS_TEXT);
   consent=new CheckBox(this);consent.setText("I agree to share my live location during this delivery, including while my screen is locked or I use other apps. I can stop at any time.");consent.setTextSize(16);consent.setPadding(0,20,0,20);root.addView(consent);
   startButton=button("Start delivery & share location",this::start);startButton.setEnabled(false);
   stopButton=button("Stop sharing / end delivery",()->{if(TrackingService.running)startService(new Intent(this,TrackingService.class).setAction(TrackingService.STOP));else{String id=store.get("active");if(id.isEmpty())id=store.get("pending");StopJob.mark(this,id);}consent.setChecked(false);});
   button("Refresh approval and session",this::refresh);
   button("Phone location & battery settings",()->startActivity(new Intent(Settings.ACTION_SETTINGS)));
   button("Sign in again / change account",()->{if(TrackingService.running){store.put("message","Stop sharing before signing in again.");return;}task(()->{if(!store.get("active").isEmpty())StopJob.mark(this,store.get("active"));if(store.get("pending").isEmpty())try{Api.call(store,"/api/auth/logout","POST",null);}catch(Exception ignored){}store.token("");approved=false;main.post(this::render);});});
   text("Force-stop, power-off, missing permissions or manufacturer battery controls can interrupt tracking. The dashboard marks old locations unavailable after 45 seconds.",13);
   text("Stop sharing stops GPS immediately. If offline, the last position stays on the dashboard until the server confirms the stop. No route history is collected.",13);
  }
 }
 private void checkAccount(String base,String phone)throws Exception{Api.validateBase(base);if((!store.get("pending").isEmpty()||!store.get("active").isEmpty())&&(!base.equals(store.get("base"))||!phone.equals(store.get("phone"))))throw new Exception("Use the same server and phone number to finish stopping the previous delivery.");}
 private void refresh(){task(()->{JSONObject me=Api.call(store,"/api/driver/me","GET",null);approved=me.getBoolean("approved");JSONObject delivery=me.optJSONObject("delivery");if(!TrackingService.running){String remote=delivery==null?"":delivery.getString("id");if(store.get("pending").isEmpty())store.put("active",remote);store.put("message",!store.get("pending").isEmpty()?"GPS stopped. Waiting for server confirmation.":!remote.isEmpty()?"A previous delivery is open. End it before starting again.":approved?"Approved. Not sharing your location.":"Waiting for dispatcher approval.");}});}
 private void start(){if(!approved||TrackingService.running||!store.get("active").isEmpty()||!store.get("pending").isEmpty())return;if(!consent.isChecked()){store.put("message","Agree to location sharing first.");return;}String ref=value(reference);if(ref.isEmpty()||ref.length()>80){store.put("message","Enter a delivery reference, up to 80 characters.");return;}
  List<String> permissions=new ArrayList<>();if(checkSelfPermission(Manifest.permission.ACCESS_FINE_LOCATION)!=PackageManager.PERMISSION_GRANTED&&checkSelfPermission(Manifest.permission.ACCESS_COARSE_LOCATION)!=PackageManager.PERMISSION_GRANTED){permissions.add(Manifest.permission.ACCESS_FINE_LOCATION);permissions.add(Manifest.permission.ACCESS_COARSE_LOCATION);}if(Build.VERSION.SDK_INT>=33&&checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS)!=PackageManager.PERMISSION_GRANTED)permissions.add(Manifest.permission.POST_NOTIFICATIONS);
  if(!permissions.isEmpty()){requestPermissions(permissions.toArray(new String[0]),100);return;}NotificationManager notifications=getSystemService(NotificationManager.class);NotificationChannel channel=notifications.getNotificationChannel("tracking");if(!notifications.areNotificationsEnabled()||(channel!=null&&channel.getImportance()==NotificationManager.IMPORTANCE_NONE)){store.put("message","Enable the app’s location-sharing notifications in phone Settings.");return;}
  LocationManager location=getSystemService(LocationManager.class);if(!location.isProviderEnabled(LocationManager.GPS_PROVIDER)&&!location.isProviderEnabled(LocationManager.NETWORK_PROVIDER)){store.put("message","Enable your phone’s Location setting, then tap Start.");startActivity(new Intent(Settings.ACTION_LOCATION_SOURCE_SETTINGS));return;}
  store.put("reference",ref);store.put("message","Starting delivery…");startButton.setEnabled(false);startForegroundService(new Intent(this,TrackingService.class).setAction(TrackingService.START));
 }
 @Override public void onRequestPermissionsResult(int code,String[] permissions,int[] grants){super.onRequestPermissionsResult(code,permissions,grants);if(code==100)store.put("message","Permission request completed. Tap Start again when location and notifications are allowed.");}
}
