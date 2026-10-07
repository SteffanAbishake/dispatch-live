package com.dispatch.driver;
import android.app.job.*;
import android.content.*;
import java.util.concurrent.*;

public class StopJob extends JobService {
 private final ExecutorService executor=Executors.newSingleThreadExecutor();
 static void enqueue(Context context){context.getSystemService(JobScheduler.class).schedule(new JobInfo.Builder(42,new ComponentName(context,StopJob.class)).setRequiredNetworkType(JobInfo.NETWORK_TYPE_ANY).setBackoffCriteria(10000,JobInfo.BACKOFF_POLICY_EXPONENTIAL).build());}
 // Both local stop and notification stop use this same durable pending-stop record.
 static void mark(Context context,String id){if(id.isEmpty())return;Store store=new Store(context);store.put("pending",id);store.put("message","GPS stopped. Waiting for the server to confirm the end.");enqueue(context);}
 @Override public boolean onStartJob(JobParameters params){executor.submit(()->{Store store=new Store(this);String id=store.get("pending");boolean retry=false;try{if(!id.isEmpty()){Api.call(store,"/api/driver/deliveries/"+id+"/end","POST",null);clear(store,id);}}catch(Api.Failure e){if(e.status==404)clear(store,id);else if(e.status==401||e.status==403)store.put("message","GPS stopped. Sign in again with the same number to confirm the stop.");else retry=true;}catch(Exception e){retry=true;}jobFinished(params,retry);});return true;}
 private static void clear(Store store,String id){if(store.get("pending").equals(id)){store.remove("pending");if(store.get("active").equals(id))store.remove("active");store.put("message","Sharing stopped. The saved location has been cleared.");}}
 @Override public boolean onStopJob(JobParameters params){return true;}
 @Override public void onDestroy(){executor.shutdown();super.onDestroy();}
}
