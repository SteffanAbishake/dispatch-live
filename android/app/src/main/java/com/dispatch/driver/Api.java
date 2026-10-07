package com.dispatch.driver;
import java.net.*;
import java.io.*;
import java.nio.charset.StandardCharsets;
import org.json.JSONObject;
final class Api {
 static class Failure extends IOException {final int status;Failure(int status,String message){super(message);this.status=status;}}
 static void validateBase(String base)throws Exception{URI uri=new URI(base);boolean https="https".equals(uri.getScheme());boolean emulator=BuildConfig.DEBUG&&"http".equals(uri.getScheme())&&("10.0.2.2".equals(uri.getHost())||"localhost".equals(uri.getHost()));if((!https&&!emulator)||uri.getHost()==null||uri.getUserInfo()!=null||uri.getQuery()!=null||uri.getFragment()!=null)throw new IOException("Use an HTTPS server address. Debug builds allow HTTP only on the emulator host.");}
 static JSONObject call(Store store,String path,String method,JSONObject body)throws Exception{
  String base=store.get("base");validateBase(base);HttpURLConnection connection=(HttpURLConnection)new URL(base+path).openConnection();connection.setInstanceFollowRedirects(false);connection.setConnectTimeout(10000);connection.setReadTimeout(10000);connection.setRequestMethod(method);connection.setRequestProperty("X-Dispatch-Request","1");connection.setRequestProperty("Content-Type","application/json");String token=store.token();if(!token.isEmpty())connection.setRequestProperty("Authorization","Bearer "+token);
  try{if(!method.equals("GET")){connection.setDoOutput(true);try(OutputStream out=connection.getOutputStream()){out.write((body==null?"{}":body.toString()).getBytes(StandardCharsets.UTF_8));}}
   int code=connection.getResponseCode();InputStream stream=code<400?connection.getInputStream():connection.getErrorStream();StringBuilder text=new StringBuilder();if(stream!=null)try(BufferedReader reader=new BufferedReader(new InputStreamReader(stream,StandardCharsets.UTF_8))){String line;while((line=reader.readLine())!=null){text.append(line);if(text.length()>1000000)throw new IOException("Server response too large.");}}
   JSONObject result;try{result=text.length()==0?new JSONObject():new JSONObject(text.toString());}catch(Exception e){result=new JSONObject();}
   if(code<200||code>=300)throw new Failure(code,result.optString("error","Request failed ("+code+")."));return result;
  }finally{connection.disconnect();}
 }
}
